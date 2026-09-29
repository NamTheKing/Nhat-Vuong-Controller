using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Access;
using NhatVuong.Application.Devices;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Policy;

/// <summary>Administrator edits of the configurable parameters (working agreement #6).</summary>
public sealed class PolicyService(
    IDataStore store,
    PolicyProvider provider,
    PolicyCache cache,
    AccessService access,
    DeviceConfigService deviceConfig)
{
    public Task<PolicySettings> GetAsync(CancellationToken ct = default) => provider.GetAsync(ct);

    public async Task<PolicySettings> UpdateAsync(PolicySettings requested, CancellationToken ct = default)
    {
        if (requested.Validate() is { } invalidField)
        {
            throw new ValidationException("InvalidPolicy", $"Policy field {invalidField} is out of range.");
        }

        await provider.GetAsync(ct);
        var stored = await store.Query<PolicySettings>()
            .Where(p => p.Id == PolicySettings.SingletonId)
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("PolicyMissing", "Policy row is missing.");

        var marginChanged = stored.ControlMarginMinutes != requested.ControlMarginMinutes;

        stored.ControlMarginMinutes = requested.ControlMarginMinutes;
        stored.AutoOffIdleMinutes = requested.AutoOffIdleMinutes;
        stored.LongRunAlertHours = requested.LongRunAlertHours;
        stored.MinSetpoint = requested.MinSetpoint;
        stored.MaxSetpoint = requested.MaxSetpoint;
        stored.OperatingStart = requested.OperatingStart;
        stored.OperatingEnd = requested.OperatingEnd;
        stored.DisconnectAlertMinutes = requested.DisconnectAlertMinutes;
        stored.DefaultPreCoolLeadMinutes = requested.DefaultPreCoolLeadMinutes;
        stored.CommandTimeoutSeconds = requested.CommandTimeoutSeconds;
        stored.StateFreshnessSeconds = requested.StateFreshnessSeconds;
        stored.LecturerPrecedenceSeconds = requested.LecturerPrecedenceSeconds;
        stored.DeviceOfflineThresholdSeconds = requested.DeviceOfflineThresholdSeconds;
        stored.MaxLanGrantMinutes = requested.MaxLanGrantMinutes;
        stored.ScheduleCatchUpMinutes = requested.ScheduleCatchUpMinutes;
        stored.CampusTimeZone = requested.CampusTimeZone;

        if (marginChanged)
        {
            // Grants are materialised rows (AD-2), so a new margin means re-deriving the timetable grants.
            await access.RematerializeTimetableGrantsAsync(stored, ct);
        }

        await store.SaveChangesAsync(ct);
        cache.Set(stored);

        // Cached schedules and offline thresholds are derived from policy; push the new absolute values (AD-12).
        await deviceConfig.PublishAllAsync(ct);
        return stored.Clone();
    }
}
