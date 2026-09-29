using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Tests;

public class DomainPolicyTests
{
    private static readonly PolicySettings Policy = new() { MinSetpoint = 20, MaxSetpoint = 30 };

    [Fact(DisplayName = "US-14-1: a setpoint below the minimum is rejected naming the threshold")]
    public void BelowMinimumIsRejected()
    {
        var decision = CommandPolicy.Evaluate(CommandAction.SetTemperature, "18", Policy, withinOperatingHours: true);
        Assert.Equal(RejectionReason.BelowMinimumSetpoint, decision.Rejection);
        Assert.Contains("20", decision.Detail);
    }

    [Theory(DisplayName = "FR-B2: setpoints within policy are normalised to 0.5 °C steps")]
    [InlineData("24", "24.0")]
    [InlineData("24,3", "24.5")]
    [InlineData("20", "20.0")]
    [InlineData("30", "30.0")]
    public void InRangeSetpointIsNormalised(string input, string expected)
    {
        var decision = CommandPolicy.Evaluate(CommandAction.SetTemperature, input, Policy, true);
        Assert.True(decision.IsAllowed);
        Assert.Equal(expected, decision.NormalizedValue);
    }

    [Theory(DisplayName = "FR-B2/B3: invalid values are rejected")]
    [InlineData(CommandAction.SetTemperature, "31")]
    [InlineData(CommandAction.SetTemperature, "abc")]
    [InlineData(CommandAction.SetMode, "Heat")]
    [InlineData(CommandAction.SetFanSpeed, "Turbo")]
    public void InvalidValuesAreRejected(CommandAction action, string value)
    {
        Assert.False(CommandPolicy.Evaluate(action, value, Policy, true).IsAllowed);
    }

    [Fact(DisplayName = "US-13-2: an on-command outside operating hours is rejected; off is always allowed")]
    public void OnOutsideHoursRejected()
    {
        Assert.Equal(RejectionReason.OutsideOperatingHours, CommandPolicy.Evaluate(CommandAction.PowerOn, null, Policy, false).Rejection);
        Assert.True(CommandPolicy.Evaluate(CommandAction.PowerOff, null, Policy, false).IsAllowed);
    }

    [Theory(DisplayName = "FR-D2: operating hours are half-open and may cross midnight")]
    [InlineData(6, 22, 5, 59, false)]
    [InlineData(6, 22, 6, 0, true)]
    [InlineData(6, 22, 21, 59, true)]
    [InlineData(6, 22, 22, 0, false)]
    [InlineData(22, 6, 23, 0, true)]
    [InlineData(22, 6, 12, 0, false)]
    [InlineData(0, 0, 3, 0, true)]
    public void OperatingHoursContainment(int start, int end, int hour, int minute, bool expected)
    {
        Assert.Equal(expected, OperatingHours.Contains(new TimeOnly(start, 0), new TimeOnly(end, 0), new TimeOnly(hour, minute)));
    }

    [Fact(DisplayName = "FR-A5: commands conflict only within the same setting category")]
    public void ConflictCategories()
    {
        Assert.True(CommandPolicy.Conflicts(CommandAction.PowerOn, null, CommandAction.PowerOff, null));
        Assert.True(CommandPolicy.Conflicts(CommandAction.SetTemperature, "24.0", CommandAction.SetTemperature, "27.0"));
        Assert.False(CommandPolicy.Conflicts(CommandAction.SetTemperature, "24.0", CommandAction.SetTemperature, "24.0"));
        Assert.False(CommandPolicy.Conflicts(CommandAction.SetTemperature, "24.0", CommandAction.SetFanSpeed, "High"));
    }

    [Fact(DisplayName = "FR-A3: the control window is the class plus the margin on both sides")]
    public void ControlWindowAddsMargin()
    {
        var start = new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero);
        var entry = new TimetableEntry { ExternalId = "x", StartsAt = start, EndsAt = start.AddHours(2) };
        var (from, to) = ControlWindow.For(entry, new PolicySettings { ControlMarginMinutes = 15 });
        Assert.Equal(start.AddMinutes(-15), from);
        Assert.Equal(start.AddHours(2).AddMinutes(15), to);
    }

    [Fact(DisplayName = "AD-8: campus local time converts to UTC at the edge (UTC+7)")]
    public void CampusClockConverts()
    {
        var clock = CampusClock.For(new PolicySettings());
        var utc = clock.FromLocal(new DateTime(2026, 10, 5, 8, 0, 0));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero), utc);
        Assert.Equal(8, clock.ToLocal(utc).Hour);
        Assert.True(clock.IsWithinOperatingHours(utc, new PolicySettings()));
        Assert.Single(clock.ClosingTimesBetween(utc, utc.AddHours(24), new PolicySettings()));
    }

    [Fact(DisplayName = "AD-6: policy rejects an offline threshold that would race the server's silence detection")]
    public void PolicyValidation()
    {
        Assert.Null(new PolicySettings().Validate());
        Assert.Equal(nameof(PolicySettings.DeviceOfflineThresholdSeconds), new PolicySettings { DeviceOfflineThresholdSeconds = 40 }.Validate());
        Assert.Equal(nameof(PolicySettings.MaxSetpoint), new PolicySettings { MinSetpoint = 25, MaxSetpoint = 22 }.Validate());
        Assert.Equal(nameof(PolicySettings.CampusTimeZone), new PolicySettings { CampusTimeZone = "Mars/Olympus" }.Validate());
    }

    [Fact(DisplayName = "FR-C3: connectivity is Offline when stale, Fault with an error code, else Online")]
    public void ConnectivityDerivation()
    {
        var now = DateTimeOffset.UtcNow;
        var fresh = TimeSpan.FromSeconds(30);
        Assert.Equal(Connectivity.Online, new DeviceState { IsConnected = true, LastSeenAt = now }.GetConnectivity(now, fresh));
        Assert.Equal(Connectivity.Fault, new DeviceState { IsConnected = true, LastSeenAt = now, ActiveErrorCode = "E5" }.GetConnectivity(now, fresh));
        Assert.Equal(Connectivity.Offline, new DeviceState { IsConnected = true, LastSeenAt = now.AddMinutes(-2) }.GetConnectivity(now, fresh));
        Assert.Equal(Connectivity.Offline, new DeviceState { IsConnected = false, LastSeenAt = now }.GetConnectivity(now, fresh));
    }
}
