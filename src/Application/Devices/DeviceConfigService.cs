using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Policy;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Devices;

/// <summary>
/// Builds and pushes each module's offline configuration: cached schedule for the next 24 hours, the offline
/// threshold, the grant-signing public key and revoked grant ids (US-25, AD-3, AD-6, AD-12).
/// </summary>
public sealed class DeviceConfigService(
    IDataStore store,
    PolicyProvider policyProvider,
    IDevicePort devicePort,
    IGrantSigner signer,
    TimeProvider time,
    ILogger<DeviceConfigService> logger)
{
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(24);

    public async Task PublishForDeviceAsync(Device device, CancellationToken ct = default)
    {
        var config = await BuildAsync(device.RoomId, ct);
        await PublishAsync(device.HardwareId, config, ct);
    }

    public async Task PublishForRoomAsync(Guid roomId, CancellationToken ct = default)
    {
        var devices = await store.Query<Device>().Where(d => d.RoomId == roomId).Select(d => d.HardwareId).ToListAsync(ct);
        if (devices.Count == 0)
        {
            return;
        }

        var config = await BuildAsync(roomId, ct);
        foreach (var hardwareId in devices)
        {
            await PublishAsync(hardwareId, config, ct);
        }
    }

    public async Task PublishAllAsync(CancellationToken ct = default)
    {
        var rooms = await store.Query<Device>().Select(d => d.RoomId).Distinct().ToListAsync(ct);
        foreach (var roomId in rooms)
        {
            await PublishForRoomAsync(roomId, ct);
        }
    }

    public async Task<DeviceConfig> BuildAsync(Guid roomId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var until = now + Horizon;
        var policy = await policyProvider.GetAsync(ct);
        var clock = CampusClock.For(policy);
        var actions = new List<CachedAction>();

        // Post-class auto-off (FR-D1): only where no class follows within X minutes.
        var windowStart = now - policy.AutoOffIdle;
        var entries = await store.Query<TimetableEntry>()
            .Where(e => e.RoomId == roomId && e.EndsAt >= windowStart && e.StartsAt < until)
            .OrderBy(e => e.StartsAt)
            .ToListAsync(ct);
        foreach (var entry in entries)
        {
            var offAt = entry.EndsAt + policy.AutoOffIdle;
            if (offAt < now || offAt >= until || entry.AutoOffHandledAt is not null)
            {
                continue;
            }

            var followed = entries.Any(other => other.Id != entry.Id && other.StartsAt > entry.EndsAt && other.StartsAt <= offAt);
            if (!followed)
            {
                actions.Add(new CachedAction(entry.Id, offAt, CommandAction.PowerOff, "auto-off"));
            }
        }

        // Campus closing time (FR-D2).
        foreach (var closing in clock.ClosingTimesBetween(now, until, policy))
        {
            actions.Add(new CachedAction(ClosingActionId(closing), closing, CommandAction.PowerOff, "closing"));
        }

        // Pre-cool (FR-D3), only where the policy would allow the on-command at that time.
        var preCools = await store.Query<PreCoolSchedule>()
            .Where(p => p.RoomId == roomId && p.Status == PreCoolStatus.Pending && p.DueAt >= now && p.DueAt < until)
            .ToListAsync(ct);
        actions.AddRange(preCools
            .Where(p => clock.IsWithinOperatingHours(p.DueAt, policy))
            .Select(p => new CachedAction(p.Id, p.DueAt, CommandAction.PowerOn, "pre-cool")));

        var revoked = await store.Query<AccessGrant>()
            .Where(g => g.RoomId == roomId && g.RevokedAt != null && g.RevokedReason != "Expired" && g.ValidTo > now)
            .Select(g => g.Id)
            .ToListAsync(ct);

        return new DeviceConfig(
            now,
            signer.PublicKey,
            policy.DeviceOfflineThresholdSeconds,
            actions.OrderBy(a => a.At).ToList(),
            revoked);
    }

    /// <summary>Deterministic id so the server and a module agree on which closing action a replayed fact refers to.</summary>
    public static Guid ClosingActionId(DateTimeOffset closingUtc)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"closing:{closingUtc.UtcDateTime:O}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private async Task PublishAsync(string hardwareId, DeviceConfig config, CancellationToken ct)
    {
        if (!devicePort.IsConnected)
        {
            return;
        }

        try
        {
            await devicePort.PublishConfigAsync(hardwareId, config, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Publishing config to {HardwareId} failed", hardwareId);
        }
    }
}
