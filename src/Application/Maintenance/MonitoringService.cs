using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Policy;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Maintenance;

/// <summary>
/// Alerts on abnormal operation no error code reports (US-18, FR-D4). Evaluated periodically by the scheduler
/// against the observed projection (AD-4).
/// </summary>
public sealed class MonitoringService(IDataStore store, PolicyProvider policyProvider, MaintenanceService maintenance, TimeProvider time)
{
    /// <summary>
    /// A connection is presumed lost when nothing has been heard for twice the freshness bound. This is shorter than
    /// the module's offline threshold (validated in <see cref="PolicySettings.Validate"/>), so the server stops
    /// scheduling for a device before the device starts running its cached schedule (AD-6, F-9).
    /// </summary>
    public async Task<int> DetectSilentDevicesAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var silentSince = now - policy.StateFreshness * 2;
        var silent = await store.Query<DeviceState>()
            .Where(s => s.IsConnected && s.LastSeenAt != null && s.LastSeenAt < silentSince)
            .ToListAsync(ct);
        foreach (var state in silent)
        {
            state.IsConnected = false;
            state.ConnectivityChangedAt = state.LastSeenAt;
        }

        if (silent.Count > 0)
        {
            await store.SaveChangesAsync(ct);
        }

        return silent.Count;
    }

    /// <summary>US-18: disconnected for more than the threshold during working hours. Time outside working hours does not count.</summary>
    public async Task<int> RaiseDisconnectAlertsAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var clock = CampusClock.For(policy);
        if (!clock.IsWithinOperatingHours(now, policy))
        {
            return 0;
        }

        var threshold = TimeSpan.FromMinutes(policy.DisconnectAlertMinutes);
        var disconnectedBefore = now - threshold;
        var opening = clock.LastOpeningAt(now, policy);
        var candidates = await store.Query<Device>()
            .Where(d => d.State != null && !d.State.IsConnected && !d.State.DisconnectAlerted
                        && d.State.ConnectivityChangedAt != null && d.State.ConnectivityChangedAt <= disconnectedBefore)
            .ToListAsync(ct);

        var raised = 0;
        foreach (var device in candidates)
        {
            var state = device.State!;
            var countedFrom = state.ConnectivityChangedAt!.Value > opening ? state.ConnectivityChangedAt.Value : opening;
            if (now - countedFrom < threshold)
            {
                continue;
            }

            state.DisconnectAlerted = true;
            var body = Text.Get("Notify_Disconnect_Body", device.Name, device.Room?.Code ?? "?", policy.DisconnectAlertMinutes);
            await maintenance.RecordAsync(device, IncidentKind.ProlongedDisconnect, MaintenanceService.DisconnectCode, body, now, ct);
            raised++;
        }

        if (raised > 0)
        {
            await store.SaveChangesAsync(ct);
        }

        return raised;
    }

    /// <summary>FR-D4 / US-12-2: a unit running continuously for more than N hours is recorded and alerted, once per run.</summary>
    public async Task<int> RaiseLongRunAlertsAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var onSinceBefore = now - TimeSpan.FromHours(policy.LongRunAlertHours);
        var running = await store.Query<Device>()
            .Where(d => d.State != null && d.State.Power == PowerState.On && !d.State.LongRunAlerted
                        && d.State.OnSince != null && d.State.OnSince <= onSinceBefore)
            .ToListAsync(ct);

        foreach (var device in running)
        {
            device.State!.LongRunAlerted = true;
            var body = Text.Get("Notify_LongRun_Body", device.Name, device.Room?.Code ?? "?", policy.LongRunAlertHours);
            await maintenance.RecordAsync(device, IncidentKind.LongRun, MaintenanceService.LongRunCode, body, now, ct);
        }

        if (running.Count > 0)
        {
            await store.SaveChangesAsync(ct);
        }

        return running.Count;
    }
}
