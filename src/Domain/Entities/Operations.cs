namespace NhatVuong.Domain.Entities;

public class TimetableEntry
{
    public Guid Id { get; set; }

    /// <summary>The <c>timetable_id</c> column from the imported file.</summary>
    public required string ExternalId { get; set; }
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }
    public Guid LecturerId { get; set; }
    public User? Lecturer { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public Guid ImportBatchId { get; set; }

    /// <summary>Set once the post-class auto-off (FR-D1) has been evaluated, so it runs once and survives restarts.</summary>
    public DateTimeOffset? AutoOffHandledAt { get; set; }
}

/// <summary>
/// The single source of control rights (AD-2). Timetable import and temporary grants both write rows here;
/// authorising a command is a point-in-time lookup against this table.
/// </summary>
public class AccessGrant
{
    public Guid Id { get; set; }
    public Guid SubjectUserId { get; set; }
    public User? Subject { get; set; }
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public GrantSource Source { get; set; }
    public Guid? TimetableEntryId { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsEffectiveAt(DateTimeOffset instant) =>
        ValidFrom <= instant && instant < ValidTo && (RevokedAt is null || instant < RevokedAt);
}

/// <summary>
/// One row per command or replayed device fact (FR-F5). Written by the command pipeline only (AD-7):
/// the pipeline may move a row from Pending to its final result; nothing deletes one.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; }

    /// <summary>Server-generated command id (AD-9), or the device event id for a replayed fact (AD-5).</summary>
    public Guid CommandId { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public UserRole? ActorRole { get; set; }
    public CommandSource Source { get; set; }
    public Guid? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public CommandAction Action { get; set; }
    public string? Value { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public CommandResult Result { get; set; }
    public RejectionReason Rejection { get; set; }
    public string? Detail { get; set; }
    public Guid? GrantId { get; set; }
}

public class Incident
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public IncidentKind Kind { get; set; }

    /// <summary>Vendor error code verbatim for device errors; a fixed code for system alerts.</summary>
    public required string Code { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset LastOccurredAt { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public IncidentStatus Status { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolvedByName { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}

public class Notification
{
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public required string Category { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public Guid? IncidentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>
/// A continuous On period derived from observed power transitions — the producer contract behind
/// the monthly runtime report (FR-F6).
/// </summary>
public class RuntimeSession
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public class PreCoolSchedule
{
    public Guid Id { get; set; }
    /// <summary>Plain id, no foreign key: a timetable re-import replaces entries and cancels their schedules.</summary>
    public Guid TimetableEntryId { get; set; }
    public Guid RoomId { get; set; }
    public Guid RequestedBy { get; set; }
    public int LeadMinutes { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public PreCoolStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Outcome { get; set; }
}
