using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Policy;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Devices;

public sealed record DeviceView(
    Device Device,
    Connectivity Connectivity,
    bool IsStale,
    bool CanControlNow,
    DateTimeOffset? AccessFrom,
    DateTimeOffset? AccessUntil);

/// <summary>
/// What a user sees in the device list (US-08, US-10). Administrators and maintenance staff see every device;
/// other users see rooms they hold a grant for today. <see cref="DeviceView.CanControlNow"/> is advisory only —
/// the server re-authorises every command (NFR-05).
/// </summary>
public sealed class DeviceQueryService(IDataStore store, PolicyProvider policyProvider, TimeProvider time)
{
    public async Task<List<DeviceView>> ListForAsync(Actor actor, Guid? roomId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var grants = await GrantsTodayAsync(actor, now, policy, ct);

        var query = store.Query<Device>();
        if (roomId is { } rid)
        {
            query = query.Where(d => d.RoomId == rid);
        }

        if (!SeesAllDevices(actor))
        {
            var rooms = grants.Select(g => g.RoomId).Distinct().ToList();
            query = query.Where(d => rooms.Contains(d.RoomId));
        }

        var devices = await query.OrderBy(d => d.Room!.Code).ThenBy(d => d.Name).ToListAsync(ct);
        return devices.Select(d => View(d, grants, now, policy)).ToList();
    }

    public async Task<DeviceView> GetForAsync(Actor actor, Guid deviceId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var device = await store.Query<Device>().Where(d => d.Id == deviceId).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException("DeviceNotFound", "Device not found.");
        var grants = await GrantsTodayAsync(actor, now, policy, ct);
        if (!SeesAllDevices(actor) && grants.All(g => g.RoomId != device.RoomId))
        {
            throw new ForbiddenException("NoAccess", "No rights for this room today.");
        }

        return View(device, grants, now, policy);
    }

    private static bool SeesAllDevices(Actor actor) => actor.Role is UserRole.Administrator or UserRole.MaintenanceStaff;

    private async Task<List<AccessGrant>> GrantsTodayAsync(Actor actor, DateTimeOffset now, PolicySettings policy, CancellationToken ct)
    {
        if (actor.UserId is not { } userId)
        {
            return [];
        }

        var endOfDay = CampusClock.For(policy).StartOfLocalDay(now).AddDays(1);
        return await store.Query<AccessGrant>()
            .Where(g => g.SubjectUserId == userId && g.ValidTo > now && g.ValidFrom < endOfDay && (g.RevokedAt == null || g.RevokedAt > now))
            .ToListAsync(ct);
    }

    private static DeviceView View(Device device, List<AccessGrant> grants, DateTimeOffset now, PolicySettings policy)
    {
        var state = device.State ?? new DeviceState { DeviceId = device.Id };
        var roomGrants = grants.Where(g => g.RoomId == device.RoomId).ToList();
        var current = roomGrants.Where(g => g.IsEffectiveAt(now)).OrderByDescending(g => g.ValidTo).FirstOrDefault();
        var next = current ?? roomGrants.Where(g => g.ValidFrom > now).OrderBy(g => g.ValidFrom).FirstOrDefault();
        return new DeviceView(
            device,
            state.GetConnectivity(now, policy.StateFreshness),
            !state.IsFresh(now, policy.StateFreshness),
            current is not null,
            next?.ValidFrom,
            next?.ValidTo);
    }
}
