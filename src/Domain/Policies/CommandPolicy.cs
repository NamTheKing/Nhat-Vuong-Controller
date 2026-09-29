using System.Globalization;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Domain.Policies;

public readonly record struct PolicyDecision(RejectionReason Rejection, string? NormalizedValue, string? Detail)
{
    public bool IsAllowed => Rejection == RejectionReason.None;

    public static PolicyDecision Allow(string? value) => new(RejectionReason.None, value, null);

    public static PolicyDecision Reject(RejectionReason reason, string detail) => new(reason, null, detail);
}

/// <summary>Setpoint, mode and operating-hour policy checks (FR-B2, FR-B3, FR-B4, FR-D2). Pure; evaluated only on the server.</summary>
public static class CommandPolicy
{
    public static PolicyDecision Evaluate(CommandAction action, string? value, PolicySettings policy, bool withinOperatingHours)
    {
        switch (action)
        {
            case CommandAction.PowerOn:
                return withinOperatingHours
                    ? PolicyDecision.Allow(null)
                    : PolicyDecision.Reject(
                        RejectionReason.OutsideOperatingHours,
                        $"Operating hours are {policy.OperatingStart:HH\\:mm}-{policy.OperatingEnd:HH\\:mm}.");

            case CommandAction.PowerOff:
                return PolicyDecision.Allow(null);

            case CommandAction.SetTemperature:
                if (!TryParseTemperature(value, out var celsius))
                {
                    return PolicyDecision.Reject(RejectionReason.InvalidValue, "Temperature must be a number in °C.");
                }

                if (celsius < policy.MinSetpoint)
                {
                    return PolicyDecision.Reject(
                        RejectionReason.BelowMinimumSetpoint,
                        $"Minimum setpoint is {policy.MinSetpoint.ToString(CultureInfo.InvariantCulture)}°C.");
                }

                if (celsius > policy.MaxSetpoint)
                {
                    return PolicyDecision.Reject(
                        RejectionReason.AboveMaximumSetpoint,
                        $"Maximum setpoint is {policy.MaxSetpoint.ToString(CultureInfo.InvariantCulture)}°C.");
                }

                return PolicyDecision.Allow(celsius.ToString("0.0", CultureInfo.InvariantCulture));

            case CommandAction.SetMode:
                return Enum.TryParse<AcMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
                    ? PolicyDecision.Allow(mode.ToString())
                    : PolicyDecision.Reject(RejectionReason.InvalidValue, "Mode must be Cool, Dry or Fan.");

            case CommandAction.SetFanSpeed:
                return Enum.TryParse<FanSpeed>(value, ignoreCase: true, out var fan) && Enum.IsDefined(fan)
                    ? PolicyDecision.Allow(fan.ToString())
                    : PolicyDecision.Reject(RejectionReason.InvalidValue, "Fan speed must be Auto, Low, Medium or High.");

            default:
                return PolicyDecision.Reject(RejectionReason.InvalidValue, "Unknown action.");
        }
    }

    /// <summary>Setpoints are applied in 0.5 °C steps, the resolution of wall-split control boards.</summary>
    public static bool TryParseTemperature(string? value, out double celsius)
    {
        celsius = 0;
        if (string.IsNullOrWhiteSpace(value)
            || !double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            return false;
        }

        celsius = Math.Round(parsed * 2, MidpointRounding.AwayFromZero) / 2;
        return true;
    }

    /// <summary>Commands in the same category compete for the same board setting (used by lecturer precedence, FR-A5).</summary>
    public static string Category(CommandAction action) => action switch
    {
        CommandAction.PowerOn or CommandAction.PowerOff => "power",
        CommandAction.SetTemperature => "temperature",
        CommandAction.SetMode => "mode",
        CommandAction.SetFanSpeed => "fan",
        _ => action.ToString(),
    };

    /// <summary>Whether two commands of the same category would leave the board in different states.</summary>
    public static bool Conflicts(CommandAction a, string? valueA, CommandAction b, string? valueB) =>
        Category(a) == Category(b) && (a != b || !string.Equals(valueA, valueB, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Control window derivation for timetable grants (FR-A2, FR-A3).</summary>
public static class ControlWindow
{
    public static (DateTimeOffset From, DateTimeOffset To) For(TimetableEntry entry, PolicySettings policy) =>
        (entry.StartsAt - policy.ControlMargin, entry.EndsAt + policy.ControlMargin);
}
