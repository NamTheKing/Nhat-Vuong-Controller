namespace NhatVuong.Domain.Policies;

/// <summary>
/// UTC end to end, campus local time only at the edges (AD-8). This is the one place that converts.
/// </summary>
public sealed class CampusClock
{
    private static readonly Dictionary<string, string> WindowsFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Ho_Chi_Minh"] = "SE Asia Standard Time",
        ["Asia/Bangkok"] = "SE Asia Standard Time",
    };

    public CampusClock(TimeZoneInfo zone) => Zone = zone;

    public TimeZoneInfo Zone { get; }

    public static CampusClock For(PolicySettings policy) =>
        TryResolve(policy.CampusTimeZone, out var zone) ? new CampusClock(zone!) : new CampusClock(TimeZoneInfo.Utc);

    public static bool TryResolve(string id, out TimeZoneInfo? zone)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone))
        {
            return true;
        }

        return WindowsFallbacks.TryGetValue(id, out var windowsId) && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone);
    }

    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    /// <summary>Interprets a wall-clock time as campus local time and returns the UTC instant.</summary>
    public DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = Zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    public DateTimeOffset StartOfLocalDay(DateTimeOffset utc) => FromLocal(ToLocal(utc).Date);

    public bool IsWithinOperatingHours(DateTimeOffset utc, PolicySettings policy) =>
        OperatingHours.Contains(policy.OperatingStart, policy.OperatingEnd, TimeOnly.FromDateTime(ToLocal(utc).DateTime));

    /// <summary>The UTC instant the current (or most recent) operating period opened.</summary>
    public DateTimeOffset LastOpeningAt(DateTimeOffset utc, PolicySettings policy)
    {
        var local = ToLocal(utc);
        var opening = FromLocal(local.Date + policy.OperatingStart.ToTimeSpan());
        return opening <= utc ? opening : opening.AddDays(-1);
    }

    /// <summary>UTC closing instants of the operating period that fall within [from, to).</summary>
    public IEnumerable<DateTimeOffset> ClosingTimesBetween(DateTimeOffset from, DateTimeOffset to, PolicySettings policy)
    {
        if (policy.OperatingStart == policy.OperatingEnd)
        {
            yield break;
        }

        var day = ToLocal(from).Date.AddDays(-1);
        var lastDay = ToLocal(to).Date.AddDays(1);
        for (; day <= lastDay; day = day.AddDays(1))
        {
            var closing = FromLocal(day + policy.OperatingEnd.ToTimeSpan());
            if (closing >= from && closing < to)
            {
                yield return closing;
            }
        }
    }
}

public static class OperatingHours
{
    /// <summary>Half-open [start, end) in local time; supports windows that cross midnight; start == end means always open.</summary>
    public static bool Contains(TimeOnly start, TimeOnly end, TimeOnly time)
    {
        if (start == end)
        {
            return true;
        }

        return start < end ? time >= start && time < end : time >= start || time < end;
    }
}
