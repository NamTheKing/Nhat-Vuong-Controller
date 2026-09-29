using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NhatVuong.Adapters.Rest.Security;
using NhatVuong.Application.Maintenance;
using NhatVuong.Application.Notifications;
using NhatVuong.Application.Reporting;
using NhatVuong.Contracts.Api;
using Dom = NhatVuong.Domain;
using Wire = NhatVuong.Contracts;

namespace NhatVuong.Adapters.Rest.Endpoints;

/// <summary>Incidents (US-16, US-17, US-18), notifications, audit (US-22), reports (US-23) and latency metrics (NFR-01).</summary>
internal static class OperationsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var maintenance = api.MapGroup("/incidents").RequireAuthorization(Policies.Maintenance);
        maintenance.MapGet("", async (Wire.IncidentStatus? status, Guid? deviceId, MaintenanceService service, CancellationToken ct) =>
            (await service.ListAsync(status is { } s ? (Dom.IncidentStatus)(int)s : null, deviceId, ct)).Select(i => i.ToDto()));
        maintenance.MapPost("/{id:guid}/resolve", async (Guid id, ResolveIncidentRequest r, HttpContext http, MaintenanceService service, CancellationToken ct) =>
        {
            await service.ResolveAsync(id, http.User.ToActor(), r.Note, ct);
            return Results.NoContent();
        });

        api.MapGet("/notifications", async (bool? unreadOnly, HttpContext http, NotificationService service, CancellationToken ct) =>
            (await service.ListAsync(http.User.ToActor().RequireUserId(), unreadOnly ?? false, ct)).Select(n => n.ToDto()));
        api.MapGet("/notifications/unread-count", async (HttpContext http, NotificationService service, CancellationToken ct) =>
            new UnreadCountDto(await service.CountUnreadAsync(http.User.ToActor().RequireUserId(), ct)));
        api.MapPost("/notifications/{id:guid}/read", async (Guid id, HttpContext http, NotificationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(http.User.ToActor().RequireUserId(), id, ct);
            return Results.NoContent();
        });
        api.MapPost("/notifications/read-all", async (HttpContext http, NotificationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(http.User.ToActor().RequireUserId(), null, ct);
            return Results.NoContent();
        });

        var admin = api.MapGroup("").RequireAuthorization(Policies.Administrator);
        admin.MapGet("/audit", async (
            Guid? deviceId, Guid? actorUserId, DateTimeOffset? from, DateTimeOffset? to, Wire.CommandResult? result, int? page, int? pageSize,
            ReportingService reports, CancellationToken ct) =>
        {
            var paged = await reports.QueryAuditAsync(
                new AuditQuery(deviceId, actorUserId, from, to, result is { } r ? (Dom.CommandResult)(int)r : null, page ?? 1, pageSize ?? 50), ct);
            return new PagedDto<AuditEntryDto>(paged.Items.Select(a => a.ToDto()).ToList(), paged.Page, paged.PageSize, paged.Total);
        });

        admin.MapGet("/reports/runtime", async (int year, int month, ReportingService reports, CancellationToken ct) =>
            (await reports.GetMonthlyRuntimeAsync(year, month, ct)).ToDto());

        admin.MapGet("/metrics/command-latency", async (int? last, ReportingService reports, CancellationToken ct) =>
            (await reports.GetCommandLatencyAsync(last ?? 100, ct)).ToDto());
    }
}
