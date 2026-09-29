using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NhatVuong.Adapters.Rest.Security;
using NhatVuong.Application.Commands;
using NhatVuong.Application.Devices;
using NhatVuong.Contracts.Api;
using Dom = NhatVuong.Domain;

namespace NhatVuong.Adapters.Rest.Endpoints;

/// <summary>Devices, registration (US-19), commands (US-06, US-07, US-11) and LAN grants (US-24).</summary>
internal static class DeviceEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/devices", async (Guid? roomId, HttpContext http, DeviceQueryService devices, CancellationToken ct) =>
            (await devices.ListForAsync(http.User.ToActor(), roomId, ct)).Select(d => d.ToDto()));

        api.MapGet("/devices/{id:guid}", async (Guid id, HttpContext http, DeviceQueryService devices, CancellationToken ct) =>
            (await devices.GetForAsync(http.User.ToActor(), id, ct)).ToDto());

        // The command pipeline answers with an outcome; a rejection is a normal outcome, not a transport error.
        api.MapPost("/devices/{id:guid}/commands", async (Guid id, SendCommandRequest r, HttpContext http, CommandService commands, CancellationToken ct) =>
        {
            var outcome = await commands.ExecuteAsync(
                new CommandRequest(http.User.ToActor(), id, (Dom.CommandAction)(int)r.Action, r.Value), ct);
            return outcome.ToDto();
        });

        api.MapPost("/rooms/{id:guid}/commands", async (Guid id, SendCommandRequest r, HttpContext http, CommandService commands, CancellationToken ct) =>
        {
            var outcomes = await commands.ExecuteRoomAsync(http.User.ToActor(), id, (Dom.CommandAction)(int)r.Action, r.Value, ct: ct);
            return new RoomCommandResultDto(outcomes.Count, outcomes.Count(o => o.Succeeded), outcomes.Select(o => o.ToDto()).ToList());
        });

        api.MapPost("/devices/{id:guid}/lan-grant", async (Guid id, LanGrantRequest r, HttpContext http, OfflineGrantService grants, CancellationToken ct) =>
            (await grants.IssueAsync(http.User.ToActor(), id, r.ClientPublicKey, ct)).ToDto());

        var admin = api.MapGroup("/devices").RequireAuthorization(Policies.Administrator);

        admin.MapPost("", async (RegisterDeviceRequest r, HttpContext http, DeviceRegistryService registry, CancellationToken ct) =>
        {
            var registered = await registry.RegisterAsync(http.User.ToActor(), r.QrCode, r.RoomId, r.Name, ct);
            return Results.Created(
                $"{ApiRoutes.Prefix}/devices/{registered.Device.Id}",
                new DeviceCredentialsResponse(registered.Device.Id, registered.Device.HardwareId, registered.MqttUsername, registered.MqttPassword));
        });

        admin.MapPut("/{id:guid}", async (Guid id, UpdateDeviceRequest r, DeviceRegistryService registry, CancellationToken ct) =>
        {
            await registry.UpdateAsync(id, r.Name, r.RoomId, ct);
            return Results.NoContent();
        });

        admin.MapPost("/{id:guid}/credentials", async (Guid id, DeviceRegistryService registry, CancellationToken ct) =>
        {
            var rotated = await registry.RotateCredentialsAsync(id, ct);
            return new DeviceCredentialsResponse(rotated.Device.Id, rotated.Device.HardwareId, rotated.MqttUsername, rotated.MqttPassword);
        });

        admin.MapDelete("/{id:guid}", async (Guid id, DeviceRegistryService registry, CancellationToken ct) =>
        {
            await registry.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
