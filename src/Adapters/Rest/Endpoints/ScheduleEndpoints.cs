using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NhatVuong.Adapters.Rest.Security;
using NhatVuong.Application.Scheduling;
using NhatVuong.Application.Timetables;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Adapters.Rest.Endpoints;

/// <summary>Timetable import (US-20), my classes and pre-cool schedules (US-15).</summary>
internal static class ScheduleEndpoints
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    public static void Map(RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/timetable").RequireAuthorization(Policies.Administrator);

        admin.MapPost("/import", async (IFormFile file, TimetableImportService import, CancellationToken ct) =>
            {
                if (file.Length is 0 or > MaxUploadBytes)
                {
                    return ProblemMapping.Problem(StatusCodes.Status400BadRequest, "InvalidFileSize", "Upload a non-empty file of at most 5 MB.");
                }

                await using var stream = file.OpenReadStream();
                var rows = await TimetableFileReader.ReadAsync(stream, file.FileName, ct);
                var result = (await import.ImportAsync(rows, ct)).ToDto();

                // US-20-2: per-row errors, nothing written.
                return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
            })
            .DisableAntiforgery();

        admin.MapGet("", async (DateTimeOffset? from, DateTimeOffset? to, Guid? roomId, Guid? lecturerId, TimetableImportService import, TimeProvider time, CancellationToken ct) =>
        {
            var start = from ?? time.GetUtcNow().AddDays(-1);
            var end = to ?? start.AddDays(14);
            return (await import.ListAsync(start, end, roomId, lecturerId, ct)).Select(e => e.ToDto());
        });

        admin.MapGet("/template", () =>
        {
            var csv = string.Join(',', TimetableImportService.Columns) + "\n"
                      + "HK1-INT101-01,A101,giangvien1@nhatvuong.edu.vn,2026-10-05 07:30,2026-10-05 09:30\n";
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "timetable-template.csv");
        });

        api.MapGet("/me/classes", async (int? days, HttpContext http, SchedulingService scheduling, CancellationToken ct) =>
            (await scheduling.ListMyClassesAsync(http.User.ToActor(), days ?? 7, ct)).Select(c => c.ToDto()));

        api.MapPost("/precool", async (CreatePreCoolRequest r, HttpContext http, SchedulingService scheduling, CancellationToken ct) =>
        {
            var schedule = await scheduling.CreatePreCoolAsync(http.User.ToActor(), r.TimetableEntryId, r.LeadMinutes, ct);
            return Results.Created($"{ApiRoutes.Prefix}/precool/{schedule.Id}", schedule.ToDto());
        });

        api.MapDelete("/precool/{id:guid}", async (Guid id, HttpContext http, SchedulingService scheduling, CancellationToken ct) =>
        {
            await scheduling.CancelPreCoolAsync(http.User.ToActor(), id, ct);
            return Results.NoContent();
        });
    }
}
