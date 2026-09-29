using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NhatVuong.Adapters.Rest.Security;
using NhatVuong.Application.Access;
using NhatVuong.Application.Administration;
using NhatVuong.Application.Policy;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Adapters.Rest.Endpoints;

/// <summary>Buildings, rooms (US-21), temporary grants (US-04) and policy (working agreement #6).</summary>
internal static class CampusEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/buildings", async (ReferenceDataService data, CancellationToken ct) =>
            (await data.ListBuildingsAsync(ct)).Select(b => b.ToDto()));
        api.MapGet("/rooms", async (ReferenceDataService data, CancellationToken ct) =>
            (await data.ListRoomsAsync(ct)).Select(r => r.ToDto()));

        var admin = api.MapGroup("").RequireAuthorization(Policies.Administrator);

        admin.MapPost("/buildings", async (SaveBuildingRequest r, ReferenceDataService data, CancellationToken ct) =>
        {
            var building = await data.CreateBuildingAsync(r.Code, r.Name, ct);
            return Results.Created($"{ApiRoutes.Prefix}/buildings/{building.Id}", building.ToDto());
        });
        admin.MapPut("/buildings/{id:guid}", async (Guid id, SaveBuildingRequest r, ReferenceDataService data, CancellationToken ct) =>
            (await data.UpdateBuildingAsync(id, r.Code, r.Name, ct)).ToDto());
        admin.MapDelete("/buildings/{id:guid}", async (Guid id, ReferenceDataService data, CancellationToken ct) =>
        {
            await data.DeleteBuildingAsync(id, ct);
            return Results.NoContent();
        });

        admin.MapPost("/rooms", async (SaveRoomRequest r, ReferenceDataService data, CancellationToken ct) =>
        {
            var room = await data.CreateRoomAsync(r.BuildingId, r.Code, r.Name, ct);
            return Results.Created($"{ApiRoutes.Prefix}/rooms/{room.Id}", new { room.Id });
        });
        admin.MapPut("/rooms/{id:guid}", async (Guid id, SaveRoomRequest r, ReferenceDataService data, CancellationToken ct) =>
        {
            await data.UpdateRoomAsync(id, r.BuildingId, r.Code, r.Name, ct);
            return Results.NoContent();
        });
        admin.MapDelete("/rooms/{id:guid}", async (Guid id, ReferenceDataService data, CancellationToken ct) =>
        {
            await data.DeleteRoomAsync(id, ct);
            return Results.NoContent();
        });

        admin.MapGet("/grants", async (bool? activeOnly, Guid? userId, Guid? roomId, AccessService access, CancellationToken ct) =>
            (await access.ListAsync(activeOnly ?? true, userId, roomId, ct)).Select(g => g.ToDto()));
        admin.MapPost("/grants", async (CreateGrantRequest r, HttpContext http, AccessService access, CancellationToken ct) =>
        {
            var grant = await access.CreateTemporaryGrantAsync(http.User.ToActor(), r.UserId, r.RoomId, r.ValidFrom, r.ValidTo, r.Note, ct);
            return Results.Created($"{ApiRoutes.Prefix}/grants/{grant.Id}", new { grant.Id });
        });
        admin.MapDelete("/grants/{id:guid}", async (Guid id, AccessService access, CancellationToken ct) =>
        {
            await access.RevokeAsync(id, ct);
            return Results.NoContent();
        });

        admin.MapGet("/policy", async (PolicyService policy, CancellationToken ct) => (await policy.GetAsync(ct)).ToDto());
        admin.MapPut("/policy", async (PolicyDto r, PolicyService policy, CancellationToken ct) =>
            (await policy.UpdateAsync(r.ToDomain(), ct)).ToDto());

        api.MapGet("/control-bounds", async (PolicyProvider policy, CancellationToken ct) =>
        {
            var p = await policy.GetAsync(ct);
            return new ControlBoundsDto(p.MinSetpoint, p.MaxSetpoint, p.OperatingStart, p.OperatingEnd, p.CampusTimeZone);
        });
    }
}
