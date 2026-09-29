using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Access;
using NhatVuong.Application.Commands;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Policy;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Scheduling;

public sealed record ClassView(
    Guid EntryId,
    string ExternalId,
    Guid RoomId,
    string RoomCode,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? PreCoolId,
    PreCoolStatus? PreCoolStatus,
    DateTimeOffset? PreCoolDueAt);

/// <summary>
/// Energy-saving schedules (US-12, US-13, US-15). Every action goes through <see cref="CommandService"/> as a
/// system actor, so it is authorised by policy and audited like a user's command (AD-1). State that must survive
/// a restart (handled/pending) lives in the database; missed actions inside the catch-up window still fire.
/// </summary>
public sealed class SchedulingService(
    IDataStore store,
    PolicyProvider policyProvider,
    AccessService access,
    CommandService commands,
    DeviceConfigService deviceConfig,
    TimeProvider time)
{
    private static readonly TimeSpan ClosingRetryInterval = TimeSpan.FromSeconds(60);

    // ---- US-15: pre-cool scheduling by the class's lecturer or a grant holder ----

    public async Task<List<ClassView>> ListMyClassesAsync(Actor actor, int days, CancellationToken ct = default)
    {
        var userId = actor.RequireUserId();
        var now = time.GetUtcNow();
        var until = now.AddDays(Math.Clamp(days, 1, 31));
        var entries = await store.Query<TimetableEntry>()
            .Where(e => e.LecturerId == userId && e.EndsAt >= now && e.StartsAt < until)
            .OrderBy(e => e.StartsAt)
            .Select(e => new { e.Id, e.ExternalId, e.RoomId, RoomCode = e.Room!.Code, e.StartsAt, e.EndsAt })
            .ToListAsync(ct);
        var ids = entries.Select(e => e.Id).ToList();
        var preCools = (await store.Query<PreCoolSchedule>()
                .Where(p => ids.Contains(p.TimetableEntryId) && p.Status != PreCoolStatus.Cancelled)
                .ToListAsync(ct))
            .GroupBy(p => p.TimetableEntryId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());

        return entries.Select(e =>
        {
            preCools.TryGetValue(e.Id, out var p);
            return new ClassView(e.Id, e.ExternalId, e.RoomId, e.RoomCode, e.StartsAt, e.EndsAt, p?.Id, p?.Status, p?.DueAt);
        }).ToList();
    }

    public async Task<PreCoolSchedule> CreatePreCoolAsync(Actor actor, Guid timetableEntryId, int? leadMinutes, CancellationToken ct = default)
    {
        var userId = actor.RequireUserId();
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var clock = CampusClock.For(policy);
        var entry = await store.Query<TimetableEntry>().Where(e => e.Id == timetableEntryId).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("TimetableEntryNotFound", "Class not found.");

        var allowed = entry.LecturerId == userId
                      || actor.IsAdministrator
                      || await access.FindEffectiveGrantAsync(userId, entry.RoomId, entry.StartsAt, ct) is not null;
        if (!allowed)
        {
            throw new ForbiddenException("NoAccess", "Only the class's lecturer or a grant holder can schedule pre-cooling.");
        }

        var lead = leadMinutes ?? policy.DefaultPreCoolLeadMinutes;
        if (lead is < 1 or > 120)
        {
            throw new ValidationException("InvalidLeadTime", "Lead time must be between 1 and 120 minutes.");
        }

        var dueAt = entry.StartsAt - TimeSpan.FromMinutes(lead);
        if (dueAt <= now)
        {
            throw new ValidationException("TooLate", "The pre-cool time has already passed.");
        }

        if (!clock.IsWithinOperatingHours(dueAt, policy))
        {
            throw new ValidationException("OutsideOperatingHours", "The pre-cool time is outside operating hours.");
        }

        if (await store.Query<PreCoolSchedule>().AnyAsync(p => p.TimetableEntryId == entry.Id && p.Status == PreCoolStatus.Pending, ct))
        {
            throw new ConflictException("PreCoolExists", "A pre-cool is already scheduled for this class.");
        }

        var schedule = new PreCoolSchedule
        {
            Id = Guid.CreateVersion7(),
            TimetableEntryId = entry.Id,
            RoomId = entry.RoomId,
            RequestedBy = userId,
            LeadMinutes = lead,
            DueAt = dueAt,
            Status = PreCoolStatus.Pending,
            CreatedAt = now,
        };
        store.Add(schedule);
        await store.SaveChangesAsync(ct);
        await deviceConfig.PublishForRoomAsync(entry.RoomId, ct);
        return schedule;
    }

    /// <summary>US-15-2: a cancelled schedule never produces a command.</summary>
    public async Task CancelPreCoolAsync(Actor actor, Guid scheduleId, CancellationToken ct = default)
    {
        var schedule = await store.Query<PreCoolSchedule>().Where(p => p.Id == scheduleId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("PreCoolNotFound", "Schedule not found.");
        if (schedule.RequestedBy != actor.UserId && !actor.IsAdministrator)
        {
            throw new ForbiddenException("NoAccess", "Only the requester or an administrator can cancel.");
        }

        if (schedule.Status != PreCoolStatus.Pending)
        {
            throw new ConflictException("PreCoolNotPending", "The schedule is no longer pending.");
        }

        schedule.Status = PreCoolStatus.Cancelled;
        schedule.CompletedAt = time.GetUtcNow();
        schedule.Outcome = Text.Get("PreCool_Outcome_Cancelled");
        await store.SaveChangesAsync(ct);
        await deviceConfig.PublishForRoomAsync(schedule.RoomId, ct);
    }

    // ---- Scheduler ticks ----

    /// <summary>US-12-1: X minutes after a class, with no class following and no temporary booking, the room's units switch off.</summary>
    public async Task<int> RunAutoOffAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var dueEndedBefore = now - policy.AutoOffIdle;
        var due = await store.Query<TimetableEntry>()
            .Where(e => e.AutoOffHandledAt == null && e.EndsAt <= dueEndedBefore)
            .OrderBy(e => e.EndsAt)
            .Take(200)
            .ToListAsync(ct);

        var switchedOff = 0;
        foreach (var entry in due)
        {
            var offAt = entry.EndsAt + policy.AutoOffIdle;
            entry.AutoOffHandledAt = now;
            if (now - offAt > policy.ScheduleCatchUp || await RoomInUseAsync(entry, offAt, now, ct))
            {
                continue;
            }

            var onDevices = await store.Query<Device>()
                .Where(d => d.RoomId == entry.RoomId && d.State != null && d.State.Power == PowerState.On)
                .Select(d => d.Id)
                .ToListAsync(ct);
            if (onDevices.Count == 0)
            {
                continue;
            }

            var actor = Actor.System(Text.Get("Actor_AutoOff"));
            var outcomes = await commands.ExecuteBatchAsync(
                onDevices.Select(id => new CommandRequest(actor, id, CommandAction.PowerOff, null, CommandSource.Scheduler)).ToList(), ct);
            switchedOff += outcomes.Count(o => o.Succeeded);
        }

        await store.SaveChangesAsync(ct);
        return switchedOff;
    }

    /// <summary>US-13-1: outside operating hours every running unit switches off (a hard limit, regardless of grants).</summary>
    public async Task<int> RunClosingAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        if (CampusClock.For(policy).IsWithinOperatingHours(now, policy))
        {
            return 0;
        }

        var retryBefore = now - ClosingRetryInterval;
        var running = await store.Query<DeviceState>()
            .Where(s => s.Power == PowerState.On && s.IsConnected
                        && (s.LastSystemOffAttemptAt == null || s.LastSystemOffAttemptAt < retryBefore))
            .ToListAsync(ct);
        if (running.Count == 0)
        {
            return 0;
        }

        foreach (var state in running)
        {
            state.LastSystemOffAttemptAt = now;
        }

        await store.SaveChangesAsync(ct);
        var actor = Actor.System(Text.Get("Actor_Closing"));
        var outcomes = await commands.ExecuteBatchAsync(
            running.Select(s => new CommandRequest(actor, s.DeviceId, CommandAction.PowerOff, null, CommandSource.Scheduler)).ToList(), ct);
        return outcomes.Count(o => o.Succeeded);
    }

    /// <summary>US-15-1: at class start minus lead time, the room's units switch on.</summary>
    public async Task<int> RunPreCoolAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var clock = CampusClock.For(policy);
        var due = await store.Query<PreCoolSchedule>()
            .Where(p => p.Status == PreCoolStatus.Pending && p.DueAt <= now)
            .OrderBy(p => p.DueAt)
            .Take(200)
            .ToListAsync(ct);

        var executed = 0;
        foreach (var schedule in due)
        {
            schedule.CompletedAt = now;
            if (now - schedule.DueAt > policy.ScheduleCatchUp)
            {
                schedule.Status = PreCoolStatus.Skipped;
                schedule.Outcome = Text.Get("PreCool_Outcome_Missed");
                continue;
            }

            if (!clock.IsWithinOperatingHours(now, policy))
            {
                schedule.Status = PreCoolStatus.Skipped;
                schedule.Outcome = Text.Get("PreCool_Outcome_OutsideHours");
                continue;
            }

            var requester = await store.Query<User>().Where(u => u.Id == schedule.RequestedBy).Select(u => u.FullName).FirstOrDefaultAsync(ct);
            var deviceIds = await store.Query<Device>().Where(d => d.RoomId == schedule.RoomId).Select(d => d.Id).ToListAsync(ct);
            var actor = Actor.System(Text.Get("Actor_PreCool", requester ?? "?"));
            var outcomes = await commands.ExecuteBatchAsync(
                deviceIds.Select(id => new CommandRequest(actor, id, CommandAction.PowerOn, null, CommandSource.Scheduler)).ToList(), ct);
            var succeeded = outcomes.Count(o => o.Succeeded);
            schedule.Status = succeeded > 0 || deviceIds.Count == 0 ? PreCoolStatus.Done : PreCoolStatus.Failed;
            schedule.Outcome = Text.Get("PreCool_Outcome_Done", succeeded, deviceIds.Count);
            executed++;
        }

        await store.SaveChangesAsync(ct);
        return executed;
    }

    private async Task<bool> RoomInUseAsync(TimetableEntry entry, DateTimeOffset offAt, DateTimeOffset now, CancellationToken ct)
    {
        var followed = await store.Query<TimetableEntry>().AnyAsync(
            e => e.RoomId == entry.RoomId && e.Id != entry.Id
                 && ((e.StartsAt > entry.EndsAt && e.StartsAt <= offAt) || (e.StartsAt <= now && e.EndsAt > now)),
            ct);
        if (followed)
        {
            return true;
        }

        // A seminar or make-up class booked by temporary grant keeps the room in use.
        return await store.Query<AccessGrant>().AnyAsync(
            g => g.RoomId == entry.RoomId && g.Source == GrantSource.Temporary
                 && g.ValidFrom <= now && g.ValidTo > now && (g.RevokedAt == null || g.RevokedAt > now),
            ct);
    }
}
