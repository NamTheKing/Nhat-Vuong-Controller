namespace NhatVuong.Domain.Policies;

/// <summary>
/// Administrator-configurable policy (SRS note 6.5-2, working agreement #6). Lives only on the server (AD-12);
/// devices and clients receive derived absolute times and bounds, never these values.
/// Defaults are the values stated in the SRS where it states one.
/// </summary>
public class PolicySettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>FR-A3: minutes before and after a class during which the lecturer may control the room.</summary>
    public int ControlMarginMinutes { get; set; } = 15;

    /// <summary>FR-D1 parameter X: idle minutes after a class before the room's units switch off.</summary>
    public int AutoOffIdleMinutes { get; set; } = 15;

    /// <summary>FR-D4 parameter N: continuous runtime that raises an alert.</summary>
    public int LongRunAlertHours { get; set; } = 8;

    /// <summary>FR-B4: minimum permitted setpoint.</summary>
    public double MinSetpoint { get; set; } = 20;

    /// <summary>FR-B2: maximum permitted setpoint.</summary>
    public double MaxSetpoint { get; set; } = 30;

    /// <summary>FR-D2: campus operating hours, campus local time.</summary>
    public TimeOnly OperatingStart { get; set; } = new(6, 0);
    public TimeOnly OperatingEnd { get; set; } = new(22, 0);

    /// <summary>FR-E4: disconnection that raises an alert during working hours.</summary>
    public int DisconnectAlertMinutes { get; set; } = 30;

    /// <summary>FR-D3: default lead time for switching a unit on before a class.</summary>
    public int DefaultPreCoolLeadMinutes { get; set; } = 10;

    /// <summary>FR-B5: how long to wait for the device's reply before reporting it as not responding.</summary>
    public int CommandTimeoutSeconds { get; set; } = 5;

    /// <summary>AD-4 freshness bound: a projection older than this is shown as Offline/unknown.</summary>
    public int StateFreshnessSeconds { get; set; } = 30;

    /// <summary>FR-A5: window in which a lecturer's command overrides a conflicting class-monitor command.</summary>
    public int LecturerPrecedenceSeconds { get; set; } = 120;

    /// <summary>AD-6: a module runs its cached schedule only after losing the server for longer than this.</summary>
    public int DeviceOfflineThresholdSeconds { get; set; } = 120;

    /// <summary>AD-3: upper bound on a LAN grant's lifetime, independent of class length.</summary>
    public int MaxLanGrantMinutes { get; set; } = 120;

    /// <summary>Restart safety: scheduled actions missed by less than this still fire; older ones are skipped.</summary>
    public int ScheduleCatchUpMinutes { get; set; } = 15;

    /// <summary>AD-8: the campus zone used at the UI and timetable-import edges.</summary>
    public string CampusTimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    public TimeSpan ControlMargin => TimeSpan.FromMinutes(ControlMarginMinutes);
    public TimeSpan AutoOffIdle => TimeSpan.FromMinutes(AutoOffIdleMinutes);
    public TimeSpan StateFreshness => TimeSpan.FromSeconds(StateFreshnessSeconds);
    public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);
    public TimeSpan ScheduleCatchUp => TimeSpan.FromMinutes(ScheduleCatchUpMinutes);

    public PolicySettings Clone() => (PolicySettings)MemberwiseClone();

    /// <summary>Returns the name of the first invalid field, or null when the settings are coherent.</summary>
    public string? Validate()
    {
        if (ControlMarginMinutes is < 0 or > 240) return nameof(ControlMarginMinutes);
        if (AutoOffIdleMinutes is < 1 or > 240) return nameof(AutoOffIdleMinutes);
        if (LongRunAlertHours is < 1 or > 72) return nameof(LongRunAlertHours);
        if (MinSetpoint is < 16 or > 30) return nameof(MinSetpoint);
        if (MaxSetpoint < MinSetpoint || MaxSetpoint > 32) return nameof(MaxSetpoint);
        if (DisconnectAlertMinutes is < 1 or > 1440) return nameof(DisconnectAlertMinutes);
        if (DefaultPreCoolLeadMinutes is < 1 or > 120) return nameof(DefaultPreCoolLeadMinutes);
        if (CommandTimeoutSeconds is < 1 or > 30) return nameof(CommandTimeoutSeconds);
        if (StateFreshnessSeconds is < 5 or > 3600) return nameof(StateFreshnessSeconds);
        if (LecturerPrecedenceSeconds is < 0 or > 3600) return nameof(LecturerPrecedenceSeconds);
        // AD-6 ordering: the server declares a device silent after 2x freshness; the device must wait longer
        // before running its cached schedule, or both would fire the same action.
        if (DeviceOfflineThresholdSeconds <= StateFreshnessSeconds * 2) return nameof(DeviceOfflineThresholdSeconds);
        if (MaxLanGrantMinutes is < 5 or > 720) return nameof(MaxLanGrantMinutes);
        if (ScheduleCatchUpMinutes is < 0 or > 240) return nameof(ScheduleCatchUpMinutes);
        if (!CampusClock.TryResolve(CampusTimeZone, out _)) return nameof(CampusTimeZone);
        return null;
    }
}
