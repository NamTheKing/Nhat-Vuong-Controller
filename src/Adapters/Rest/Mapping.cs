using NhatVuong.Application.Access;
using NhatVuong.Application.Administration;
using NhatVuong.Application.Commands;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Maintenance;
using NhatVuong.Application.Reporting;
using NhatVuong.Application.Scheduling;
using NhatVuong.Application.Timetables;
using NhatVuong.Contracts.Api;
using NhatVuong.Contracts.Lan;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;
using Wire = NhatVuong.Contracts;

namespace NhatVuong.Adapters.Rest;

/// <summary>Domain/application types to wire DTOs. Enum values are identical by contract (see Contracts/Enums.cs).</summary>
internal static class Mapping
{
    public static UserDto ToDto(this User u) => new(u.Id, u.Email, u.FullName, (Wire.UserRole)(int)u.Role, u.IsActive);

    public static BuildingDto ToDto(this Building b) => new(b.Id, b.Code, b.Name);

    public static RoomDto ToDto(this RoomView r) => new(r.Id, r.Code, r.Name, r.BuildingId, r.BuildingCode, r.BuildingName, r.DeviceCount);

    public static DeviceStateDto ToDto(this DeviceState s) => new(
        (Wire.PowerState)(int)s.Power,
        s.Setpoint,
        (Wire.AcMode)(int)s.Mode,
        (Wire.FanSpeed)(int)s.Fan,
        s.RoomTemperature,
        s.CompressorRunning,
        s.ActiveErrorCode,
        s.ObservedAt,
        s.LastSeenAt);

    public static DeviceDto ToDto(this DeviceView v)
    {
        var d = v.Device;
        var state = d.State ?? new DeviceState { DeviceId = d.Id };
        return new DeviceDto(
            d.Id,
            d.HardwareId,
            d.Name,
            d.RoomId,
            d.Room?.Code ?? string.Empty,
            d.Room?.Name ?? string.Empty,
            d.Room?.Building?.Code ?? string.Empty,
            (Wire.Connectivity)(int)v.Connectivity,
            v.IsStale,
            v.CanControlNow,
            v.AccessFrom,
            v.AccessUntil,
            !string.IsNullOrEmpty(state.LanEndpoint),
            state.ToDto());
    }

    public static CommandOutcomeDto ToDto(this CommandOutcome o) => new(
        o.CommandId,
        o.DeviceId,
        o.DeviceName,
        (Wire.CommandResult)(int)o.Result,
        (Wire.RejectionReason)(int)o.Rejection,
        o.Detail,
        o.ElapsedMs,
        o.State?.ToDto());

    public static TimetableImportResultDto ToDto(this ImportResult r) => new(
        r.Success, r.ImportedCount, r.ReplacedCount, r.Errors.Select(e => new ImportErrorDto(e.Row, e.Code, e.Detail)).ToList(), r.RangeFrom, r.RangeTo);

    public static TimetableEntryDto ToDto(this TimetableEntryView e) =>
        new(e.Id, e.ExternalId, e.RoomId, e.RoomCode, e.LecturerId, e.LecturerName, e.StartsAt, e.EndsAt);

    public static ClassDto ToDto(this ClassView c) => new(
        c.EntryId, c.ExternalId, c.RoomId, c.RoomCode, c.StartsAt, c.EndsAt, c.PreCoolId,
        c.PreCoolStatus is { } s ? (Wire.PreCoolStatus)(int)s : null, c.PreCoolDueAt);

    public static PreCoolDto ToDto(this PreCoolSchedule p) =>
        new(p.Id, p.TimetableEntryId, p.RoomId, p.LeadMinutes, p.DueAt, (Wire.PreCoolStatus)(int)p.Status, p.Outcome);

    public static GrantDto ToDto(this GrantView g) => new(
        g.Id, g.SubjectUserId, g.SubjectName, g.SubjectEmail, (Wire.UserRole)(int)g.SubjectRole, g.RoomId, g.RoomCode,
        g.ValidFrom, g.ValidTo, (Wire.GrantSource)(int)g.Source, g.Note, g.RevokedAt, g.RevokedReason);

    public static PolicyDto ToDto(this PolicySettings p) => new(
        p.ControlMarginMinutes, p.AutoOffIdleMinutes, p.LongRunAlertHours, p.MinSetpoint, p.MaxSetpoint, p.OperatingStart, p.OperatingEnd,
        p.DisconnectAlertMinutes, p.DefaultPreCoolLeadMinutes, p.CommandTimeoutSeconds, p.StateFreshnessSeconds,
        p.LecturerPrecedenceSeconds, p.DeviceOfflineThresholdSeconds, p.MaxLanGrantMinutes, p.ScheduleCatchUpMinutes, p.CampusTimeZone);

    public static PolicySettings ToDomain(this PolicyDto p) => new()
    {
        ControlMarginMinutes = p.ControlMarginMinutes,
        AutoOffIdleMinutes = p.AutoOffIdleMinutes,
        LongRunAlertHours = p.LongRunAlertHours,
        MinSetpoint = p.MinSetpoint,
        MaxSetpoint = p.MaxSetpoint,
        OperatingStart = p.OperatingStart,
        OperatingEnd = p.OperatingEnd,
        DisconnectAlertMinutes = p.DisconnectAlertMinutes,
        DefaultPreCoolLeadMinutes = p.DefaultPreCoolLeadMinutes,
        CommandTimeoutSeconds = p.CommandTimeoutSeconds,
        StateFreshnessSeconds = p.StateFreshnessSeconds,
        LecturerPrecedenceSeconds = p.LecturerPrecedenceSeconds,
        DeviceOfflineThresholdSeconds = p.DeviceOfflineThresholdSeconds,
        MaxLanGrantMinutes = p.MaxLanGrantMinutes,
        ScheduleCatchUpMinutes = p.ScheduleCatchUpMinutes,
        CampusTimeZone = p.CampusTimeZone,
    };

    public static IncidentDto ToDto(this IncidentView i) => new(
        i.Id, i.DeviceId, i.DeviceName, i.RoomCode, (Wire.IncidentKind)(int)i.Kind, i.Code, i.Message, i.OccurredAt, i.LastOccurredAt,
        i.OccurrenceCount, (Wire.IncidentStatus)(int)i.Status, i.ResolvedByName, i.ResolvedAt, i.ResolutionNote);

    public static NotificationDto ToDto(this Domain.Entities.Notification n) =>
        new(n.Id, n.Category, n.Title, n.Body, n.IncidentId, n.CreatedAt, n.ReadAt is not null);

    public static AuditEntryDto ToDto(this AuditEntry a) => new(
        a.Id, a.CommandId, a.ActorUserId, a.ActorName, a.ActorRole is { } r ? (Wire.UserRole)(int)r : null, (Wire.CommandSource)(int)a.Source,
        a.DeviceId, a.DeviceName, (Wire.CommandAction)(int)a.Action, a.Value, a.RequestedAt, a.CompletedAt,
        (Wire.CommandResult)(int)a.Result, (Wire.RejectionReason)(int)a.Rejection, a.Detail);

    public static RuntimeReportDto ToDto(this RuntimeReport r) => new(
        r.Year, r.Month, r.HasData, r.TotalHours,
        r.Buildings.Select(b => new BuildingRuntimeDto(
            b.BuildingId, b.Code, b.Name, b.Hours, b.Rooms.Select(x => new RoomRuntimeDto(x.RoomId, x.RoomCode, x.RoomName, x.Hours)).ToList())).ToList());

    public static LatencyStatsDto ToDto(this LatencyStats s) => new(s.Count, s.P50Ms, s.P95Ms, s.MaxMs, s.BudgetMs, s.WithinBudget);

    public static LanGrantResponse ToDto(this IssuedLanGrant g) =>
        new(new LanGrantDto(g.Payload, g.Signature, g.Algorithm), g.ValidTo, g.LanEndpoint, g.LanCertThumbprint, g.ServerPublicKey);
}
