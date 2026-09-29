using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Policy;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Reporting;

public sealed record RoomRuntime(Guid RoomId, string RoomCode, string RoomName, double Hours);

public sealed record BuildingRuntime(Guid BuildingId, string Code, string Name, double Hours, IReadOnlyList<RoomRuntime> Rooms);

public sealed record RuntimeReport(int Year, int Month, bool HasData, double TotalHours, IReadOnlyList<BuildingRuntime> Buildings);

public sealed record AuditQuery(
    Guid? DeviceId, Guid? ActorUserId, DateTimeOffset? From, DateTimeOffset? To, CommandResult? Result, int Page = 1, int PageSize = 50);

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record LatencyStats(int Count, double P50Ms, double P95Ms, double MaxMs, double BudgetMs, bool WithinBudget);

/// <summary>Monthly runtime per room and building (US-23) and audit queries (US-22).</summary>
public sealed class ReportingService(IDataStore store, PolicyProvider policyProvider, TimeProvider time)
{
    /// <summary>NFR-01: tap to device state change, P95.</summary>
    public const double LatencyBudgetMs = 3000;

    public async Task<RuntimeReport> GetMonthlyRuntimeAsync(int year, int month, CancellationToken ct = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            throw new ValidationException("InvalidMonth", "Year or month out of range.");
        }

        var policy = await policyProvider.GetAsync(ct);
        var clock = CampusClock.For(policy);
        var from = clock.FromLocal(new DateTime(year, month, 1));
        var to = clock.FromLocal(new DateTime(year, month, 1).AddMonths(1));
        var now = time.GetUtcNow();

        var sessions = await store.Query<RuntimeSession>()
            .Where(s => s.StartedAt < to && (s.EndedAt == null || s.EndedAt > from))
            .ToListAsync(ct);

        var perDevice = new Dictionary<Guid, double>();
        foreach (var session in sessions)
        {
            var start = session.StartedAt > from ? session.StartedAt : from;
            var endRaw = session.EndedAt ?? now;
            var end = endRaw < to ? endRaw : to;
            if (end > start)
            {
                perDevice[session.DeviceId] = perDevice.GetValueOrDefault(session.DeviceId) + (end - start).TotalHours;
            }
        }

        if (perDevice.Count == 0)
        {
            // US-23-2: no data is a normal answer, not an error.
            return new RuntimeReport(year, month, false, 0, []);
        }

        var deviceIds = perDevice.Keys.ToList();
        var placement = await store.Query<Device>()
            .Where(d => deviceIds.Contains(d.Id))
            .Select(d => new
            {
                d.Id,
                d.RoomId,
                RoomCode = d.Room!.Code,
                RoomName = d.Room.Name,
                d.Room.BuildingId,
                BuildingCode = d.Room.Building!.Code,
                BuildingName = d.Room.Building.Name,
            })
            .ToListAsync(ct);

        var buildings = placement
            .GroupBy(p => new { p.BuildingId, p.BuildingCode, p.BuildingName })
            .Select(b =>
            {
                var rooms = b.GroupBy(p => new { p.RoomId, p.RoomCode, p.RoomName })
                    .Select(r => new RoomRuntime(r.Key.RoomId, r.Key.RoomCode, r.Key.RoomName, Round(r.Sum(p => perDevice[p.Id]))))
                    .OrderBy(r => r.RoomCode)
                    .ToList();
                return new BuildingRuntime(b.Key.BuildingId, b.Key.BuildingCode, b.Key.BuildingName, Round(rooms.Sum(r => r.Hours)), rooms);
            })
            .OrderBy(b => b.Code)
            .ToList();

        return new RuntimeReport(year, month, true, Round(buildings.Sum(b => b.Hours)), buildings);
    }

    public async Task<Paged<AuditEntry>> QueryAuditAsync(AuditQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var q = store.Query<AuditEntry>();
        if (query.DeviceId is { } deviceId)
        {
            q = q.Where(a => a.DeviceId == deviceId);
        }

        if (query.ActorUserId is { } actorId)
        {
            q = q.Where(a => a.ActorUserId == actorId);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            q = q.Where(a => a.RequestedAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            q = q.Where(a => a.RequestedAt < toUtc);
        }

        if (query.Result is { } result)
        {
            q = q.Where(a => a.Result == result);
        }

        var total = await store.CountAsync(q, ct);
        var items = await q.OrderByDescending(a => a.RequestedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new Paged<AuditEntry>(items, page, size, total);
    }

    /// <summary>NFR-01 verification: latency over the last N completed app commands, from audit timestamps.</summary>
    public async Task<LatencyStats> GetCommandLatencyAsync(int last, CancellationToken ct = default)
    {
        var sample = await store.Query<AuditEntry>()
            .Where(a => a.Source == CommandSource.App && a.Result == CommandResult.Succeeded && a.CompletedAt != null)
            .OrderByDescending(a => a.RequestedAt)
            .Take(Math.Clamp(last, 1, 10_000))
            .Select(a => new { a.RequestedAt, a.CompletedAt })
            .ToListAsync(ct);

        var durations = sample.Select(a => (a.CompletedAt!.Value - a.RequestedAt).TotalMilliseconds).Order().ToList();
        if (durations.Count == 0)
        {
            return new LatencyStats(0, 0, 0, 0, LatencyBudgetMs, true);
        }

        var p95 = Percentile(durations, 0.95);
        return new LatencyStats(durations.Count, Percentile(durations, 0.5), p95, durations[^1], LatencyBudgetMs, p95 <= LatencyBudgetMs);
    }

    private static double Percentile(List<double> sorted, double p)
    {
        var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
        return Math.Round(sorted[Math.Clamp(rank, 0, sorted.Count - 1)], 1);
    }

    private static double Round(double hours) => Math.Round(hours, 2);
}
