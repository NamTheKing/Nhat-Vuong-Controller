using System.Globalization;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Access;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Identity;
using NhatVuong.Application.Policy;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Timetables;

/// <summary>One data row of the timetable file as text; <see cref="RowNumber"/> is the row number in the original file.</summary>
public sealed record TimetableRow(int RowNumber, string? TimetableId, string? RoomCode, string? LecturerEmail, string? StartsAt, string? EndsAt);

public sealed record ImportError(int Row, string Code, string Detail);

public sealed record ImportResult(
    bool Success,
    int ImportedCount,
    int ReplacedCount,
    IReadOnlyList<ImportError> Errors,
    DateTimeOffset? RangeFrom,
    DateTimeOffset? RangeTo);

public sealed record TimetableEntryView(
    Guid Id, string ExternalId, Guid RoomId, string RoomCode, Guid LecturerId, string LecturerName, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Timetable import (US-20). All rows are validated first; a file with any invalid row writes nothing (FR-F3).
/// A valid file atomically replaces every entry in the campus-local date range it covers, revoking the replaced
/// entries' grants and cancelling their pre-cool schedules.
/// </summary>
public sealed class TimetableImportService(
    IDataStore store,
    PolicyProvider policyProvider,
    DeviceConfigService deviceConfig,
    TimeProvider time)
{
    public static readonly IReadOnlyList<string> Columns = ["timetable_id", "room_code", "lecturer_email", "starts_at", "ends_at"];

    public const int MaxRows = 20_000;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss",
        "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm", "d/M/yyyy HH:mm",
    ];

    public async Task<ImportResult> ImportAsync(IReadOnlyList<TimetableRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0)
        {
            return Failed(new ImportError(0, "EmptyFile", "The file contains no data rows."));
        }

        if (rows.Count > MaxRows)
        {
            return Failed(new ImportError(0, "TooManyRows", $"At most {MaxRows} rows per file."));
        }

        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var clock = CampusClock.For(policy);

        var rooms = (await store.Query<Room>().ToListAsync(ct)).ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        var emails = rows.Select(r => AuthService.NormalizeEmail(r.LecturerEmail ?? string.Empty)).Where(e => e.Length > 0).Distinct().ToList();
        var users = (await store.Query<User>().Where(u => emails.Contains(u.Email)).ToListAsync(ct)).ToDictionary(u => u.Email);

        var errors = new List<ImportError>();
        var parsed = new List<(TimetableRow Row, TimetableEntry Entry)>();
        var seenIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var rowErrors = new List<ImportError>();
            var externalId = Required(row, row.TimetableId, "timetable_id", rowErrors);
            var roomCode = Required(row, row.RoomCode, "room_code", rowErrors);
            var email = Required(row, row.LecturerEmail, "lecturer_email", rowErrors);
            var startsAt = ParseTime(row, row.StartsAt, "starts_at", clock, rowErrors);
            var endsAt = ParseTime(row, row.EndsAt, "ends_at", clock, rowErrors);

            Room? room = null;
            if (roomCode is not null && !rooms.TryGetValue(roomCode, out room))
            {
                rowErrors.Add(new ImportError(row.RowNumber, "RoomNotFound", $"Room {roomCode} does not exist."));
            }

            User? lecturer = null;
            if (email is not null)
            {
                if (!users.TryGetValue(AuthService.NormalizeEmail(email), out lecturer) || !lecturer.IsActive)
                {
                    rowErrors.Add(new ImportError(row.RowNumber, "LecturerNotFound", $"No active user with email {email}."));
                    lecturer = null;
                }
                else if (lecturer.Role != UserRole.Lecturer)
                {
                    rowErrors.Add(new ImportError(row.RowNumber, "NotALecturer", $"{email} is not a lecturer."));
                    lecturer = null;
                }
            }

            if (startsAt is { } s && endsAt is { } e && e <= s)
            {
                rowErrors.Add(new ImportError(row.RowNumber, "EndBeforeStart", "ends_at must be after starts_at."));
            }

            if (externalId is not null)
            {
                if (seenIds.TryGetValue(externalId, out var firstRow))
                {
                    rowErrors.Add(new ImportError(row.RowNumber, "DuplicateTimetableId", $"timetable_id {externalId} already used on row {firstRow}."));
                }
                else
                {
                    seenIds[externalId] = row.RowNumber;
                }
            }

            if (rowErrors.Count > 0 || room is null || lecturer is null || startsAt is null || endsAt is null || externalId is null)
            {
                errors.AddRange(rowErrors);
                continue;
            }

            parsed.Add((row, new TimetableEntry
            {
                Id = Guid.CreateVersion7(),
                ExternalId = externalId,
                RoomId = room.Id,
                LecturerId = lecturer.Id,
                StartsAt = startsAt.Value,
                EndsAt = endsAt.Value,
            }));
        }

        errors.AddRange(FindOverlaps(parsed));
        if (errors.Count > 0)
        {
            return Failed([.. errors.OrderBy(e => e.Row)]);
        }

        // Replacement range: whole campus-local days covered by the file.
        var rangeFrom = clock.FromLocal(clock.ToLocal(parsed.Min(p => p.Entry.StartsAt)).Date);
        var rangeTo = clock.FromLocal(clock.ToLocal(parsed.Max(p => p.Entry.EndsAt)).Date.AddDays(1));

        var replaced = await store.Query<TimetableEntry>()
            .Where(e => e.StartsAt >= rangeFrom && e.StartsAt < rangeTo)
            .ToListAsync(ct);
        var replacedIds = replaced.Select(e => e.Id).ToList();

        foreach (var grant in await store.Query<AccessGrant>()
                     .Where(g => g.TimetableEntryId != null && replacedIds.Contains(g.TimetableEntryId.Value) && g.RevokedAt == null)
                     .ToListAsync(ct))
        {
            grant.RevokedAt = now;
            grant.RevokedReason = AccessService.ReplacedReason;
        }

        foreach (var preCool in await store.Query<PreCoolSchedule>()
                     .Where(p => replacedIds.Contains(p.TimetableEntryId) && p.Status == PreCoolStatus.Pending)
                     .ToListAsync(ct))
        {
            preCool.Status = PreCoolStatus.Cancelled;
            preCool.CompletedAt = now;
            preCool.Outcome = Text.Get("PreCool_Outcome_Replaced");
        }

        foreach (var entry in replaced)
        {
            store.Remove(entry);
        }

        var batchId = Guid.CreateVersion7();
        foreach (var (_, entry) in parsed)
        {
            entry.ImportBatchId = batchId;
            store.Add(entry);
            store.Add(AccessService.GrantFor(entry, policy, now));
        }

        // One SaveChanges: the replacement and the new rows commit together or not at all.
        await store.SaveChangesAsync(ct);
        await deviceConfig.PublishAllAsync(ct);

        return new ImportResult(true, parsed.Count, replaced.Count, [], rangeFrom, rangeTo);
    }

    public Task<List<TimetableEntryView>> ListAsync(DateTimeOffset from, DateTimeOffset to, Guid? roomId, Guid? lecturerId, CancellationToken ct = default)
    {
        // Npgsql accepts only UTC offsets for timestamptz parameters (AD-8).
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var query = store.Query<TimetableEntry>().Where(e => e.EndsAt > fromUtc && e.StartsAt < toUtc);
        if (roomId is { } rid)
        {
            query = query.Where(e => e.RoomId == rid);
        }

        if (lecturerId is { } lid)
        {
            query = query.Where(e => e.LecturerId == lid);
        }

        return query
            .OrderBy(e => e.StartsAt)
            .Take(2000)
            .Select(e => new TimetableEntryView(e.Id, e.ExternalId, e.RoomId, e.Room!.Code, e.LecturerId, e.Lecturer!.FullName, e.StartsAt, e.EndsAt))
            .ToListAsync(ct);
    }

    private static IEnumerable<ImportError> FindOverlaps(List<(TimetableRow Row, TimetableEntry Entry)> parsed)
    {
        foreach (var group in parsed.GroupBy(p => p.Entry.RoomId))
        {
            foreach (var error in Overlaps(group, "RoomDoubleBooked", "Room already booked by row {0}."))
            {
                yield return error;
            }
        }

        foreach (var group in parsed.GroupBy(p => p.Entry.LecturerId))
        {
            foreach (var error in Overlaps(group, "LecturerDoubleBooked", "Lecturer already teaching on row {0}."))
            {
                yield return error;
            }
        }
    }

    private static IEnumerable<ImportError> Overlaps(IEnumerable<(TimetableRow Row, TimetableEntry Entry)> group, string code, string detail)
    {
        (TimetableRow Row, TimetableEntry Entry)? previous = null;
        foreach (var item in group.OrderBy(p => p.Entry.StartsAt))
        {
            if (previous is { } prev && item.Entry.StartsAt < prev.Entry.EndsAt)
            {
                yield return new ImportError(item.Row.RowNumber, code, string.Format(CultureInfo.InvariantCulture, detail, prev.Row.RowNumber));
            }

            if (previous is null || item.Entry.EndsAt > previous.Value.Entry.EndsAt)
            {
                previous = item;
            }
        }
    }

    private static string? Required(TimetableRow row, string? value, string column, List<ImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ImportError(row.RowNumber, "MissingField", $"{column} is required."));
            return null;
        }

        return value.Trim();
    }

    private static DateTimeOffset? ParseTime(TimetableRow row, string? value, string column, CampusClock clock, List<ImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ImportError(row.RowNumber, "MissingField", $"{column} is required."));
            return null;
        }

        if (!DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
        {
            errors.Add(new ImportError(row.RowNumber, "InvalidDateTime", $"{column} '{value}' is not a date-time (yyyy-MM-dd HH:mm)."));
            return null;
        }

        // AD-8: the file is in campus local time; convert once, here.
        return clock.FromLocal(local);
    }

    private static ImportResult Failed(params ImportError[] errors) => new(false, 0, 0, errors, null, null);
}
