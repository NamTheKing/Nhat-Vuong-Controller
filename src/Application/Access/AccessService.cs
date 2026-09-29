using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Devices;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Access;

public sealed record GrantView(
    Guid Id,
    Guid SubjectUserId,
    string SubjectName,
    string SubjectEmail,
    UserRole SubjectRole,
    Guid RoomId,
    string RoomCode,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    GrantSource Source,
    string? Note,
    DateTimeOffset? RevokedAt,
    string? RevokedReason);

/// <summary>
/// Control rights (US-02, US-03, US-04, US-05). Every right is an <see cref="AccessGrant"/> row;
/// authorisation is a point-in-time lookup and nothing else (AD-2).
/// </summary>
public sealed class AccessService(IDataStore store, TimeProvider time, DeviceConfigService deviceConfig)
{
    public const string ExpiredReason = "Expired";
    public const string RevokedByAdminReason = "RevokedByAdministrator";
    public const string ReplacedReason = "TimetableReplaced";

    private static readonly TimeSpan MaxTemporaryGrant = TimeSpan.FromDays(31);

    public Task<AccessGrant?> FindEffectiveGrantAsync(Guid userId, Guid roomId, DateTimeOffset at, CancellationToken ct = default) =>
        store.Query<AccessGrant>()
            .Where(g => g.SubjectUserId == userId
                        && g.RoomId == roomId
                        && g.Subject!.IsActive
                        && g.ValidFrom <= at
                        && g.ValidTo > at
                        && (g.RevokedAt == null || g.RevokedAt > at))
            .OrderByDescending(g => g.ValidTo)
            .FirstOrDefaultAsync(ct);

    /// <summary>US-04: an administrator grants a named user a room for exactly the given range.</summary>
    public async Task<AccessGrant> CreateTemporaryGrantAsync(
        Actor actor, Guid userId, Guid roomId, DateTimeOffset validFrom, DateTimeOffset validTo, string? note, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        if (validTo <= validFrom)
        {
            throw new ValidationException("InvalidRange", "The end of the range must be after its start.");
        }

        if (validTo <= now)
        {
            throw new ValidationException("RangeInPast", "The range has already ended.");
        }

        if (validTo - validFrom > MaxTemporaryGrant)
        {
            throw new ValidationException("RangeTooLong", "Temporary grants last at most 31 days.");
        }

        if (!await store.Query<User>().AnyAsync(u => u.Id == userId && u.IsActive, ct))
        {
            throw new NotFoundException("UserNotFound", "User not found or inactive.");
        }

        if (!await store.Query<Room>().AnyAsync(r => r.Id == roomId, ct))
        {
            throw new NotFoundException("RoomNotFound", "Room not found.");
        }

        var grant = new AccessGrant
        {
            Id = Guid.CreateVersion7(),
            SubjectUserId = userId,
            RoomId = roomId,
            ValidFrom = validFrom.ToUniversalTime(),
            ValidTo = validTo.ToUniversalTime(),
            Source = GrantSource.Temporary,
            CreatedBy = actor.UserId,
            CreatedAt = now,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        store.Add(grant);
        await store.SaveChangesAsync(ct);
        return grant;
    }

    /// <summary>Revocation before expiry. Pushed to the room's modules so a LAN grant under it stops working (F-6).</summary>
    public async Task RevokeAsync(Guid grantId, CancellationToken ct = default)
    {
        var grant = await store.Query<AccessGrant>().Where(g => g.Id == grantId).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("GrantNotFound", "Grant not found.");
        if (grant.RevokedAt is not null)
        {
            throw new ConflictException("GrantAlreadyRevoked", "The grant is already revoked.");
        }

        grant.RevokedAt = time.GetUtcNow();
        grant.RevokedReason = RevokedByAdminReason;
        await store.SaveChangesAsync(ct);
        await deviceConfig.PublishForRoomAsync(grant.RoomId, ct);
    }

    public Task<List<GrantView>> ListAsync(bool activeOnly, Guid? userId, Guid? roomId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var query = store.Query<AccessGrant>();
        if (activeOnly)
        {
            query = query.Where(g => g.ValidTo > now && (g.RevokedAt == null || g.RevokedAt > now));
        }

        if (userId is { } uid)
        {
            query = query.Where(g => g.SubjectUserId == uid);
        }

        if (roomId is { } rid)
        {
            query = query.Where(g => g.RoomId == rid);
        }

        return query
            .OrderBy(g => g.ValidFrom)
            .Take(500)
            .Select(g => new GrantView(
                g.Id, g.SubjectUserId, g.Subject!.FullName, g.Subject.Email, g.Subject.Role, g.RoomId, g.Room!.Code,
                g.ValidFrom, g.ValidTo, g.Source, g.Note, g.RevokedAt, g.RevokedReason))
            .ToListAsync(ct);
    }

    /// <summary>US-02/US-03: a timetable entry materialises one grant covering the class plus margin.</summary>
    public static AccessGrant GrantFor(TimetableEntry entry, PolicySettings policy, DateTimeOffset now)
    {
        var (from, to) = ControlWindow.For(entry, policy);
        return new AccessGrant
        {
            Id = Guid.CreateVersion7(),
            SubjectUserId = entry.LecturerId,
            RoomId = entry.RoomId,
            ValidFrom = from,
            ValidTo = to,
            Source = GrantSource.Timetable,
            TimetableEntryId = entry.Id,
            CreatedAt = now,
        };
    }

    /// <summary>Re-derives live timetable grants after the control margin changes. The caller saves.</summary>
    public async Task RematerializeTimetableGrantsAsync(PolicySettings policy, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var grants = await store.Query<AccessGrant>()
            .Where(g => g.Source == GrantSource.Timetable && g.RevokedAt == null && g.TimetableEntryId != null)
            .ToListAsync(ct);
        var entryIds = grants.Select(g => g.TimetableEntryId!.Value).Distinct().ToList();
        var entries = (await store.Query<TimetableEntry>().Where(e => entryIds.Contains(e.Id)).ToListAsync(ct))
            .ToDictionary(e => e.Id);

        foreach (var grant in grants)
        {
            if (!entries.TryGetValue(grant.TimetableEntryId!.Value, out var entry))
            {
                continue;
            }

            var (from, to) = ControlWindow.For(entry, policy);
            if (to <= now && grant.ValidTo <= now)
            {
                continue;
            }

            grant.ValidFrom = from;
            grant.ValidTo = to;
        }
    }

    /// <summary>
    /// US-05: rights lapse by themselves because authorisation compares against ValidTo; this sweep also records
    /// the revocation on the row so the lapse is visible without anyone acting.
    /// </summary>
    public async Task<int> SweepExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var expired = await store.Query<AccessGrant>()
            .Where(g => g.RevokedAt == null && g.ValidTo <= now)
            .Take(1000)
            .ToListAsync(ct);

        foreach (var grant in expired)
        {
            grant.RevokedAt = grant.ValidTo;
            grant.RevokedReason = ExpiredReason;
        }

        if (expired.Count > 0)
        {
            await store.SaveChangesAsync(ct);
        }

        return expired.Count;
    }
}
