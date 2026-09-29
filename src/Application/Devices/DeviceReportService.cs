using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Maintenance;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Devices;

/// <summary>A device-side action taken while offline: a LAN command or a cached schedule entry (AD-5).</summary>
public sealed record OfflineFact(
    Guid EventId,
    DateTimeOffset OccurredAt,
    CommandAction Action,
    string? Value,
    bool Success,
    CommandSource Source,
    Guid? GrantId,
    Guid? SubjectUserId,
    string? SubjectName,
    Guid? ScheduleActionId);

/// <summary>
/// Device-report ingest (the third driving adapter). Updates the observed projection (AD-4) and appends
/// replayed facts to the audit log. It never turns a report into a command (AD-5).
/// </summary>
public sealed class DeviceReportService(
    IDataStore store,
    MaintenanceService maintenance,
    DeviceConfigService deviceConfig,
    TimeProvider time)
{
    public async Task HandleStateReportAsync(
        string hardwareId, ObservedState observed, DateTimeOffset observedAt, string? lanEndpoint, string? lanThumbprint,
        CancellationToken ct = default)
    {
        var device = await FindAsync(hardwareId, ct);
        if (device is null)
        {
            return;
        }

        await ApplyObservedStateAsync(device, observed, observedAt, ct);
        var state = device.State!;
        state.LanEndpoint = lanEndpoint;
        state.LanCertThumbprint = lanThumbprint;
        await store.SaveChangesAsync(ct);
    }

    public async Task HandleConnectivityAsync(string hardwareId, bool online, DateTimeOffset at, CancellationToken ct = default)
    {
        var device = await FindAsync(hardwareId, ct);
        if (device is null)
        {
            return;
        }

        var state = EnsureState(device);
        var now = time.GetUtcNow();
        if (state.IsConnected != online)
        {
            state.IsConnected = online;
            state.ConnectivityChangedAt = now;
            if (online)
            {
                // US-18-2: reconnecting clears the pending disconnection; no alert unless one already fired.
                state.DisconnectAlerted = false;
            }
        }

        if (online)
        {
            state.LastSeenAt = now;
        }

        await store.SaveChangesAsync(ct);

        if (online)
        {
            await deviceConfig.PublishForDeviceAsync(device, ct);
        }
    }

    public async Task HandleErrorAsync(string hardwareId, string code, string? message, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        var device = await FindAsync(hardwareId, ct);
        if (device is null || string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var state = EnsureState(device);
        state.ActiveErrorCode = code.Trim();
        state.LastSeenAt = time.GetUtcNow();
        await maintenance.RecordAsync(device, IncidentKind.DeviceError, code.Trim(), message, occurredAt, ct);
        await store.SaveChangesAsync(ct);
    }

    /// <summary>AD-5: facts append to audit and runtime history; duplicates (same event id) are ignored.</summary>
    public async Task<int> HandleOfflineFactsAsync(string hardwareId, IReadOnlyList<OfflineFact> facts, CancellationToken ct = default)
    {
        var device = await FindAsync(hardwareId, ct);
        if (device is null || facts.Count == 0)
        {
            return 0;
        }

        var eventIds = facts.Select(f => f.EventId).ToList();
        var known = (await store.Query<AuditEntry>().Where(a => eventIds.Contains(a.CommandId)).Select(a => a.CommandId).ToListAsync(ct))
            .ToHashSet();

        var appended = 0;
        var openSession = await OpenSessionAsync(device.Id, ct);
        foreach (var fact in facts.OrderBy(f => f.OccurredAt))
        {
            if (!known.Add(fact.EventId))
            {
                continue;
            }

            store.Add(new AuditEntry
            {
                Id = Guid.CreateVersion7(),
                CommandId = fact.EventId,
                ActorUserId = fact.SubjectUserId,
                ActorName = fact.SubjectName
                            ?? (fact.Source == CommandSource.DeviceSchedule ? Text.Get("Actor_DeviceSchedule") : Text.Get("Actor_LanUnknown")),
                Source = fact.Source,
                DeviceId = device.Id,
                DeviceName = device.Name,
                Action = fact.Action,
                Value = fact.Value,
                RequestedAt = fact.OccurredAt,
                CompletedAt = fact.OccurredAt,
                Result = fact.Success ? CommandResult.Succeeded : CommandResult.Failed,
                Detail = Text.Get("Command_Replayed"),
                GrantId = fact.GrantId,
            });
            appended++;

            if (fact.Success)
            {
                openSession = ApplyRuntimeFact(device, fact, openSession);
                await MarkScheduleHandledAsync(fact, ct);
            }
        }

        await store.SaveChangesAsync(ct);
        return appended;
    }

    /// <summary>Applies board values the device observed. Stale observations never overwrite newer ones. The caller saves.</summary>
    public async Task ApplyObservedStateAsync(Device device, ObservedState observed, DateTimeOffset observedAt, CancellationToken ct = default)
    {
        var state = EnsureState(device);
        var now = time.GetUtcNow();
        state.LastSeenAt = now;
        if (!state.IsConnected)
        {
            state.IsConnected = true;
            state.ConnectivityChangedAt = now;
            state.DisconnectAlerted = false;
        }

        if (state.ObservedAt is { } previous && observedAt < previous)
        {
            return;
        }

        var wasOn = state.ObservedAt is not null && state.Power == PowerState.On;
        var isOn = observed.Power == PowerState.On;

        state.Power = observed.Power;
        state.Setpoint = observed.Setpoint;
        state.Mode = observed.Mode;
        state.Fan = observed.Fan;
        state.RoomTemperature = observed.RoomTemperature;
        state.CompressorRunning = isOn && observed.CompressorRunning;
        state.ObservedAt = observedAt;

        var errorCode = string.IsNullOrWhiteSpace(observed.ErrorCode) ? null : observed.ErrorCode.Trim();
        if (errorCode is not null && errorCode != state.ActiveErrorCode)
        {
            await maintenance.RecordAsync(device, IncidentKind.DeviceError, errorCode, null, observedAt, ct);
        }

        state.ActiveErrorCode = errorCode;

        if (!wasOn && isOn)
        {
            state.OnSince = observedAt;
            state.LongRunAlerted = false;
            if (await OpenSessionAsync(device.Id, ct) is null)
            {
                store.Add(new RuntimeSession { Id = Guid.CreateVersion7(), DeviceId = device.Id, StartedAt = observedAt });
            }
        }
        else if (wasOn && !isOn)
        {
            state.OnSince = null;
            state.LongRunAlerted = false;
            if (await OpenSessionAsync(device.Id, ct) is { } session)
            {
                session.EndedAt = observedAt;
            }
        }
    }

    /// <summary>Tracks the open session in memory across a replay batch; rows added in this unit of work are not yet queryable.</summary>
    private RuntimeSession? ApplyRuntimeFact(Device device, OfflineFact fact, RuntimeSession? open)
    {
        if (fact.Action == CommandAction.PowerOn && open is null)
        {
            var session = new RuntimeSession { Id = Guid.CreateVersion7(), DeviceId = device.Id, StartedAt = fact.OccurredAt };
            store.Add(session);
            return session;
        }

        if (fact.Action == CommandAction.PowerOff && open is not null && open.StartedAt <= fact.OccurredAt)
        {
            open.EndedAt = fact.OccurredAt;
            if (device.State is { } state && state.ObservedAt < fact.OccurredAt)
            {
                state.OnSince = null;
            }

            return null;
        }

        return open;
    }

    /// <summary>A schedule the module already executed offline must not fire again from the server (AD-6).</summary>
    private async Task MarkScheduleHandledAsync(OfflineFact fact, CancellationToken ct)
    {
        if (fact.ScheduleActionId is not { } actionId)
        {
            return;
        }

        var entry = await store.Query<TimetableEntry>().Where(e => e.Id == actionId).FirstOrDefaultAsync(ct);
        if (entry is { AutoOffHandledAt: null })
        {
            entry.AutoOffHandledAt = fact.OccurredAt;
        }

        var preCool = await store.Query<PreCoolSchedule>().Where(p => p.Id == actionId).FirstOrDefaultAsync(ct);
        if (preCool is { Status: PreCoolStatus.Pending })
        {
            preCool.Status = PreCoolStatus.Done;
            preCool.CompletedAt = fact.OccurredAt;
            preCool.Outcome = Text.Get("PreCool_Outcome_Device");
        }
    }

    private Task<RuntimeSession?> OpenSessionAsync(Guid deviceId, CancellationToken ct) =>
        store.Query<RuntimeSession>()
            .Where(s => s.DeviceId == deviceId && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

    private DeviceState EnsureState(Device device)
    {
        if (device.State is null)
        {
            device.State = new DeviceState { DeviceId = device.Id };
            store.Add(device.State);
        }

        return device.State;
    }

    private Task<Device?> FindAsync(string hardwareId, CancellationToken ct) =>
        store.Query<Device>().Where(d => d.HardwareId == hardwareId).FirstOrDefaultAsync(ct);
}
