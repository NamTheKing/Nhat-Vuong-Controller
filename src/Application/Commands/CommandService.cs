using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Access;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Notifications;
using NhatVuong.Application.Policy;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Commands;

public sealed record CommandRequest(Actor Actor, Guid DeviceId, CommandAction Action, string? Value, CommandSource Source = CommandSource.App);

public sealed record CommandOutcome(
    Guid CommandId,
    Guid DeviceId,
    string DeviceName,
    CommandResult Result,
    RejectionReason Rejection,
    string? Detail,
    long ElapsedMs,
    DeviceState? State)
{
    public bool Succeeded => Result == CommandResult.Succeeded;
}

/// <summary>Serialises command processing per device so simultaneous commands resolve in a defined order (FR-A5).</summary>
public sealed class DeviceLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(IEnumerable<Guid> deviceIds, CancellationToken ct)
    {
        var acquired = new List<SemaphoreSlim>();
        try
        {
            // Fixed order prevents deadlock between overlapping room-wide and single-device commands.
            foreach (var id in deviceIds.Distinct().Order())
            {
                var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(ct);
                acquired.Add(gate);
            }

            return new Releaser(acquired);
        }
        catch
        {
            new Releaser(acquired).Dispose();
            throw;
        }
    }

    private sealed class Releaser(List<SemaphoreSlim> gates) : IDisposable
    {
        public void Dispose()
        {
            foreach (var gate in gates)
            {
                gate.Release();
            }

            gates.Clear();
        }
    }
}

/// <summary>
/// The single command pipeline (AD-1). REST, scheduler and every other driving adapter submit here; this class
/// validates policy, authorises against grants, audits before dispatch (AD-7), dispatches with a correlated id
/// (AD-9), waits for the device's reply within the timeout (FR-B5) and records the outcome.
/// </summary>
public sealed class CommandService(
    IDataStore store,
    PolicyProvider policyProvider,
    AccessService access,
    IDevicePort devicePort,
    DeviceReportService reports,
    NotificationService notifications,
    DeviceLocks deviceLocks,
    TimeProvider time,
    ILogger<CommandService> logger)
{
    public async Task<CommandOutcome> ExecuteAsync(CommandRequest request, CancellationToken ct = default) =>
        (await ExecuteBatchAsync([request], ct))[0];

    /// <summary>US-11 / AD-11: one command per unit, each authorised, audited and reported independently.</summary>
    public async Task<IReadOnlyList<CommandOutcome>> ExecuteRoomAsync(
        Actor actor, Guid roomId, CommandAction action, string? value, CommandSource source = CommandSource.App, CancellationToken ct = default)
    {
        if (!await store.Query<Room>().AnyAsync(r => r.Id == roomId, ct))
        {
            throw new NotFoundException("RoomNotFound", "Room not found.");
        }

        var deviceIds = await store.Query<Device>().Where(d => d.RoomId == roomId).OrderBy(d => d.Name).Select(d => d.Id).ToListAsync(ct);
        if (deviceIds.Count == 0)
        {
            return [];
        }

        return await ExecuteBatchAsync(deviceIds.Select(id => new CommandRequest(actor, id, action, value, source)).ToList(), ct);
    }

    public async Task<IReadOnlyList<CommandOutcome>> ExecuteBatchAsync(IReadOnlyList<CommandRequest> requests, CancellationToken ct = default)
    {
        var ids = requests.Select(r => r.DeviceId).ToList();
        using var held = await deviceLocks.AcquireAsync(ids, ct);

        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var withinHours = CampusClock.For(policy).IsWithinOperatingHours(now, policy);
        var devices = (await store.Query<Device>().Where(d => ids.Contains(d.Id)).ToListAsync(ct)).ToDictionary(d => d.Id);

        var outcomes = new CommandOutcome[requests.Count];
        var dispatches = new List<(int Index, CommandRequest Request, Device Device, AuditEntry Audit)>();

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            if (!devices.TryGetValue(request.DeviceId, out var device))
            {
                throw new NotFoundException("DeviceNotFound", "Device not found.");
            }

            var audit = new AuditEntry
            {
                Id = Guid.CreateVersion7(now),
                CommandId = Guid.CreateVersion7(now),
                ActorUserId = request.Actor.UserId,
                ActorName = request.Actor.Name,
                ActorRole = request.Actor.Role,
                Source = request.Source,
                DeviceId = device.Id,
                DeviceName = device.Name,
                Action = request.Action,
                Value = request.Value,
                RequestedAt = now,
                Result = CommandResult.Pending,
            };
            store.Add(audit);

            var (rejection, detail) = await EvaluateAsync(request, device, audit, policy, withinHours, now, ct);
            if (rejection != RejectionReason.None)
            {
                // A rejected command is audited and never reaches the device, so its state is unchanged (US-14-2).
                Complete(audit, CommandResult.Rejected, rejection, detail, now);
                outcomes[i] = Outcome(audit, device);
                continue;
            }

            if (device.State is not { IsConnected: true })
            {
                Complete(audit, CommandResult.Failed, RejectionReason.DeviceOffline, Text.Get("Command_DeviceOffline"), now);
                outcomes[i] = Outcome(audit, device);
                continue;
            }

            if (!devicePort.IsConnected)
            {
                Complete(audit, CommandResult.Failed, RejectionReason.None, Text.Get("Command_GatewayDown"), now);
                outcomes[i] = Outcome(audit, device);
                continue;
            }

            dispatches.Add((i, request, device, audit));
        }

        // AD-7: the audit row exists before anything is dispatched.
        await store.SaveChangesAsync(ct);

        // Once dispatched, finish recording even if the caller goes away, so no command is left Pending.
        var sends = dispatches
            .Select(d => SendAsync(new DeviceCommand(d.Audit.CommandId, d.Device.HardwareId, d.Request.Action, d.Audit.Value, now), policy))
            .ToList();
        var results = await Task.WhenAll(sends);

        for (var k = 0; k < dispatches.Count; k++)
        {
            var (index, request, device, audit) = dispatches[k];
            var result = results[k];
            var completedAt = time.GetUtcNow();
            switch (result.Result)
            {
                case CommandResult.Succeeded:
                    Complete(audit, CommandResult.Succeeded, RejectionReason.None, null, completedAt);
                    if (result.Ack?.State is { } observed)
                    {
                        await reports.ApplyObservedStateAsync(device, observed, result.Ack.ObservedAt, CancellationToken.None);
                    }

                    if (request.Actor.Role == UserRole.Lecturer)
                    {
                        await NotifyOverriddenMonitorsAsync(request, device, audit, policy, now);
                    }

                    break;
                default:
                    Complete(audit, result.Result, RejectionReason.None, result.Detail, completedAt);
                    break;
            }

            outcomes[index] = Outcome(audit, device);
        }

        await store.SaveChangesAsync(CancellationToken.None);
        return outcomes;
    }

    /// <summary>
    /// Restart safety (F-16): a command audited but never completed because the process stopped
    /// resolves to Failed rather than staying Pending forever.
    /// </summary>
    public async Task<int> ResolveInterruptedAsync(CancellationToken ct = default)
    {
        var cutoff = time.GetUtcNow() - TimeSpan.FromMinutes(1);
        var pending = await store.Query<AuditEntry>().Where(a => a.Result == CommandResult.Pending && a.RequestedAt < cutoff).ToListAsync(ct);
        foreach (var audit in pending)
        {
            Complete(audit, CommandResult.Failed, RejectionReason.None, "Interrupted by server restart.", time.GetUtcNow());
        }

        if (pending.Count > 0)
        {
            await store.SaveChangesAsync(ct);
        }

        return pending.Count;
    }

    private async Task<(RejectionReason Reason, string? Detail)> EvaluateAsync(
        CommandRequest request, Device device, AuditEntry audit, PolicySettings policy, bool withinHours, DateTimeOffset now, CancellationToken ct)
    {
        var decision = CommandPolicy.Evaluate(request.Action, request.Value, policy, withinHours);
        if (!decision.IsAllowed)
        {
            return (decision.Rejection, LocalizedDetail(decision.Rejection, policy));
        }

        audit.Value = decision.NormalizedValue ?? request.Value;

        if (request.Actor.IsSystem)
        {
            // Scheduler-originated commands pass the same policy checks above; they act for the institution, not a grant holder.
            return (RejectionReason.None, null);
        }

        // NFR-05: rights are decided here, from grants, never by the client.
        var grant = await access.FindEffectiveGrantAsync(request.Actor.RequireUserId(), device.RoomId, now, ct);
        if (grant is null)
        {
            return (RejectionReason.NoAccess, Text.Get("Command_NoAccess"));
        }

        audit.GrantId = grant.Id;

        if (request.Actor.Role == UserRole.ClassMonitor && policy.LecturerPrecedenceSeconds > 0)
        {
            var since = now - TimeSpan.FromSeconds(policy.LecturerPrecedenceSeconds);
            var lecturerCommands = await store.Query<AuditEntry>()
                .Where(a => a.DeviceId == device.Id
                            && a.ActorRole == UserRole.Lecturer
                            && a.Result == CommandResult.Succeeded
                            && a.RequestedAt >= since)
                .OrderByDescending(a => a.RequestedAt)
                .ToListAsync(ct);

            var conflict = lecturerCommands.FirstOrDefault(a => CommandPolicy.Conflicts(a.Action, a.Value, request.Action, audit.Value));
            if (conflict is not null)
            {
                await notifications.NotifyUsersAsync(
                    [request.Actor.RequireUserId()],
                    Text.Get("Notify_Category_Control"),
                    Text.Get("Notify_PrecedenceRejected_Title"),
                    Text.Get("Notify_PrecedenceRejected_Body", device.Name, conflict.ActorName),
                    ct);
                return (RejectionReason.LecturerPrecedence, Text.Get("Command_Precedence", conflict.ActorName));
            }
        }

        return (RejectionReason.None, null);
    }

    /// <summary>FR-A5: the lecturer's command wins; class monitors whose recent conflicting command it replaced are told.</summary>
    private async Task NotifyOverriddenMonitorsAsync(CommandRequest request, Device device, AuditEntry audit, PolicySettings policy, DateTimeOffset now)
    {
        if (policy.LecturerPrecedenceSeconds <= 0)
        {
            return;
        }

        var since = now - TimeSpan.FromSeconds(policy.LecturerPrecedenceSeconds);
        var monitorCommands = await store.Query<AuditEntry>()
            .Where(a => a.DeviceId == device.Id
                        && a.ActorRole == UserRole.ClassMonitor
                        && a.Result == CommandResult.Succeeded
                        && a.RequestedAt >= since)
            .ToListAsync();

        var overridden = monitorCommands
            .Where(a => a.ActorUserId is not null && CommandPolicy.Conflicts(a.Action, a.Value, request.Action, audit.Value))
            .Select(a => a.ActorUserId!.Value)
            .Distinct()
            .ToList();

        await notifications.NotifyUsersAsync(
            overridden,
            Text.Get("Notify_Category_Control"),
            Text.Get("Notify_Overridden_Title"),
            Text.Get("Notify_Overridden_Body", request.Actor.Name, device.Name));
    }

    private async Task<(CommandResult Result, DeviceAck? Ack, string? Detail)> SendAsync(DeviceCommand command, PolicySettings policy)
    {
        try
        {
            var ack = await devicePort.SendCommandAsync(command, policy.CommandTimeout, CancellationToken.None);
            return ack.Success ? (CommandResult.Succeeded, ack, null) : (CommandResult.Failed, ack, ack.Error);
        }
        catch (DeviceTimeoutException)
        {
            return (CommandResult.Timeout, null, Text.Get("Command_Timeout", policy.CommandTimeoutSeconds));
        }
        catch (DeviceUnavailableException ex)
        {
            return (CommandResult.Failed, null, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dispatch of command {CommandId} failed", command.CommandId);
            return (CommandResult.Failed, null, "Dispatch failed.");
        }
    }

    /// <summary>The violated threshold, named in the user's language (US-14-1, NFR-07).</summary>
    private static string LocalizedDetail(RejectionReason reason, PolicySettings policy) => reason switch
    {
        RejectionReason.OutsideOperatingHours =>
            Text.Get("Reject_OutsideHours", policy.OperatingStart.ToString("HH:mm"), policy.OperatingEnd.ToString("HH:mm")),
        RejectionReason.BelowMinimumSetpoint => Text.Get("Reject_BelowMin", policy.MinSetpoint),
        RejectionReason.AboveMaximumSetpoint => Text.Get("Reject_AboveMax", policy.MaxSetpoint),
        _ => Text.Get("Reject_InvalidValue"),
    };

    private static void Complete(AuditEntry audit,CommandResult result, RejectionReason rejection, string? detail, DateTimeOffset at)
    {
        audit.Result = result;
        audit.Rejection = rejection;
        audit.Detail = detail;
        audit.CompletedAt = at;
    }

    private static CommandOutcome Outcome(AuditEntry audit, Device device) => new(
        audit.CommandId,
        device.Id,
        device.Name,
        audit.Result,
        audit.Rejection,
        audit.Detail,
        (long)((audit.CompletedAt ?? audit.RequestedAt) - audit.RequestedAt).TotalMilliseconds,
        device.State);
}
