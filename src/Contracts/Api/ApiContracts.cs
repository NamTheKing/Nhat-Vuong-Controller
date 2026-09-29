using NhatVuong.Contracts.Lan;

namespace NhatVuong.Contracts.Api;

// REST contract, version 1 (/api/v1). Errors are RFC 9457 problem+json with a stable "code" extension.

public static class ApiRoutes
{
    public const string Prefix = "/api/v1";
}

public sealed record ProblemCode(string Code, string? Detail);

// ---- Identity ----

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(Guid Id, string Email, string FullName, UserRole Role, bool IsActive);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record SaveUserRequest(string Email, string FullName, UserRole Role, bool IsActive, string? Password);

public sealed record DeleteUserResponse(bool Deleted, bool Deactivated);

// ---- Reference data ----

public sealed record BuildingDto(Guid Id, string Code, string Name);

public sealed record SaveBuildingRequest(string Code, string Name);

public sealed record RoomDto(Guid Id, string Code, string Name, Guid BuildingId, string BuildingCode, string BuildingName, int DeviceCount);

public sealed record SaveRoomRequest(Guid BuildingId, string Code, string Name);

// ---- Devices and commands ----

public sealed record DeviceStateDto(
    PowerState Power,
    double Setpoint,
    AcMode Mode,
    FanSpeed Fan,
    double? RoomTemperature,
    bool CompressorRunning,
    string? ErrorCode,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? LastSeenAt);

public sealed record DeviceDto(
    Guid Id,
    string HardwareId,
    string Name,
    Guid RoomId,
    string RoomCode,
    string RoomName,
    string BuildingCode,
    Connectivity Connectivity,
    bool IsStale,
    bool CanControlNow,
    DateTimeOffset? AccessFrom,
    DateTimeOffset? AccessUntil,
    bool LanAvailable,
    DeviceStateDto State);

public sealed record RegisterDeviceRequest(string QrCode, Guid RoomId, string? Name);

public sealed record DeviceCredentialsResponse(Guid DeviceId, string HardwareId, string MqttUsername, string MqttPassword);

public sealed record UpdateDeviceRequest(string Name, Guid RoomId);

public sealed record SendCommandRequest(CommandAction Action, string? Value);

public sealed record CommandOutcomeDto(
    Guid CommandId,
    Guid DeviceId,
    string DeviceName,
    CommandResult Result,
    RejectionReason Rejection,
    string? Detail,
    long ElapsedMs,
    DeviceStateDto? State);

public sealed record RoomCommandResultDto(int Total, int Succeeded, IReadOnlyList<CommandOutcomeDto> Items);

/// <summary>Bounds the UI needs to shape its controls. Absolute values only; the server still enforces them (AD-12, NFR-05).</summary>
public sealed record ControlBoundsDto(double MinSetpoint, double MaxSetpoint, TimeOnly OperatingStart, TimeOnly OperatingEnd, string CampusTimeZone);

public sealed record LanGrantRequest(string ClientPublicKey);

public sealed record LanGrantResponse(
    LanGrantDto Grant,
    DateTimeOffset ValidTo,
    string? LanEndpoint,
    string? LanCertThumbprint,
    string ServerPublicKey);

// ---- Timetable, grants, schedules ----

public sealed record ImportErrorDto(int Row, string Code, string Detail);

public sealed record TimetableImportResultDto(
    bool Success,
    int ImportedCount,
    int ReplacedCount,
    IReadOnlyList<ImportErrorDto> Errors,
    DateTimeOffset? RangeFrom,
    DateTimeOffset? RangeTo);

public sealed record TimetableEntryDto(
    Guid Id, string ExternalId, Guid RoomId, string RoomCode, Guid LecturerId, string LecturerName, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record ClassDto(
    Guid EntryId,
    string ExternalId,
    Guid RoomId,
    string RoomCode,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? PreCoolId,
    PreCoolStatus? PreCoolStatus,
    DateTimeOffset? PreCoolDueAt);

public sealed record CreatePreCoolRequest(Guid TimetableEntryId, int? LeadMinutes);

public sealed record PreCoolDto(Guid Id, Guid TimetableEntryId, Guid RoomId, int LeadMinutes, DateTimeOffset DueAt, PreCoolStatus Status, string? Outcome);

public sealed record GrantDto(
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

public sealed record CreateGrantRequest(Guid UserId, Guid RoomId, DateTimeOffset ValidFrom, DateTimeOffset ValidTo, string? Note);

// ---- Policy ----

public sealed record PolicyDto(
    int ControlMarginMinutes,
    int AutoOffIdleMinutes,
    int LongRunAlertHours,
    double MinSetpoint,
    double MaxSetpoint,
    TimeOnly OperatingStart,
    TimeOnly OperatingEnd,
    int DisconnectAlertMinutes,
    int DefaultPreCoolLeadMinutes,
    int CommandTimeoutSeconds,
    int StateFreshnessSeconds,
    int LecturerPrecedenceSeconds,
    int DeviceOfflineThresholdSeconds,
    int MaxLanGrantMinutes,
    int ScheduleCatchUpMinutes,
    string CampusTimeZone);

// ---- Maintenance and notifications ----

public sealed record IncidentDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string RoomCode,
    IncidentKind Kind,
    string Code,
    string? Message,
    DateTimeOffset OccurredAt,
    DateTimeOffset LastOccurredAt,
    int OccurrenceCount,
    IncidentStatus Status,
    string? ResolvedByName,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);

public sealed record ResolveIncidentRequest(string Note);

public sealed record NotificationDto(Guid Id, string Category, string Title, string Body, Guid? IncidentId, DateTimeOffset CreatedAt, bool IsRead);

public sealed record UnreadCountDto(int Count);

// ---- Audit and reports ----

public sealed record AuditEntryDto(
    Guid Id,
    Guid CommandId,
    Guid? ActorUserId,
    string ActorName,
    UserRole? ActorRole,
    CommandSource Source,
    Guid? DeviceId,
    string? DeviceName,
    CommandAction Action,
    string? Value,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    CommandResult Result,
    RejectionReason Rejection,
    string? Detail);

public sealed record PagedDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record RoomRuntimeDto(Guid RoomId, string RoomCode, string RoomName, double Hours);

public sealed record BuildingRuntimeDto(Guid BuildingId, string Code, string Name, double Hours, IReadOnlyList<RoomRuntimeDto> Rooms);

public sealed record RuntimeReportDto(int Year, int Month, bool HasData, double TotalHours, IReadOnlyList<BuildingRuntimeDto> Buildings);

public sealed record LatencyStatsDto(int Count, double P50Ms, double P95Ms, double MaxMs, double BudgetMs, bool WithinBudget);
