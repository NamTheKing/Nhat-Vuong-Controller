using System.Net.Mail;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Identity;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Administration;

public sealed record RoomView(Guid Id, string Code, string Name, Guid BuildingId, string BuildingCode, string BuildingName, int DeviceCount);

public sealed record UserDeletion(bool Deleted, bool Deactivated);

/// <summary>Rooms, buildings and users (US-21). Changes take effect system-wide on save.</summary>
public sealed class ReferenceDataService(IDataStore store, TimeProvider time)
{
    // ---- Buildings ----

    public Task<List<Building>> ListBuildingsAsync(CancellationToken ct = default) =>
        store.Query<Building>().OrderBy(b => b.Code).ToListAsync(ct);

    public async Task<Building> CreateBuildingAsync(string code, string name, CancellationToken ct = default)
    {
        var normalized = NormalizeCode(code);
        Require(name, "Name");
        if (await store.Query<Building>().AnyAsync(b => b.Code == normalized, ct))
        {
            throw new ConflictException("DuplicateBuildingCode", $"Building {normalized} already exists.");
        }

        var building = new Building { Id = Guid.CreateVersion7(), Code = normalized, Name = name.Trim() };
        store.Add(building);
        await store.SaveChangesAsync(ct);
        return building;
    }

    public async Task<Building> UpdateBuildingAsync(Guid id, string code, string name, CancellationToken ct = default)
    {
        var building = await FindAsync<Building>(b => b.Id == id, "BuildingNotFound", ct);
        var normalized = NormalizeCode(code);
        Require(name, "Name");
        if (await store.Query<Building>().AnyAsync(b => b.Code == normalized && b.Id != id, ct))
        {
            throw new ConflictException("DuplicateBuildingCode", $"Building {normalized} already exists.");
        }

        building.Code = normalized;
        building.Name = name.Trim();
        await store.SaveChangesAsync(ct);
        return building;
    }

    public async Task DeleteBuildingAsync(Guid id, CancellationToken ct = default)
    {
        var building = await FindAsync<Building>(b => b.Id == id, "BuildingNotFound", ct);
        if (await store.Query<Room>().AnyAsync(r => r.BuildingId == id, ct))
        {
            throw new ConflictException("BuildingHasRooms", "Delete or move the building's rooms first.");
        }

        store.Remove(building);
        await store.SaveChangesAsync(ct);
    }

    // ---- Rooms ----

    public Task<List<RoomView>> ListRoomsAsync(CancellationToken ct = default) =>
        store.Query<Room>()
            .OrderBy(r => r.Code)
            .Select(r => new RoomView(r.Id, r.Code, r.Name, r.BuildingId, r.Building!.Code, r.Building.Name, r.Devices.Count))
            .ToListAsync(ct);

    public async Task<Room> CreateRoomAsync(Guid buildingId, string code, string name, CancellationToken ct = default)
    {
        await FindAsync<Building>(b => b.Id == buildingId, "BuildingNotFound", ct);
        var normalized = NormalizeCode(code);
        Require(name, "Name");
        if (await store.Query<Room>().AnyAsync(r => r.Code == normalized, ct))
        {
            throw new ConflictException("DuplicateRoomCode", $"Room {normalized} already exists.");
        }

        var room = new Room { Id = Guid.CreateVersion7(), BuildingId = buildingId, Code = normalized, Name = name.Trim() };
        store.Add(room);
        await store.SaveChangesAsync(ct);
        return room;
    }

    public async Task<Room> UpdateRoomAsync(Guid id, Guid buildingId, string code, string name, CancellationToken ct = default)
    {
        var room = await FindAsync<Room>(r => r.Id == id, "RoomNotFound", ct);
        await FindAsync<Building>(b => b.Id == buildingId, "BuildingNotFound", ct);
        var normalized = NormalizeCode(code);
        Require(name, "Name");
        if (await store.Query<Room>().AnyAsync(r => r.Code == normalized && r.Id != id, ct))
        {
            throw new ConflictException("DuplicateRoomCode", $"Room {normalized} already exists.");
        }

        room.BuildingId = buildingId;
        room.Code = normalized;
        room.Name = name.Trim();
        await store.SaveChangesAsync(ct);
        return room;
    }

    /// <summary>US-21-2: a room that still has devices cannot be deleted.</summary>
    public async Task DeleteRoomAsync(Guid id, CancellationToken ct = default)
    {
        var room = await FindAsync<Room>(r => r.Id == id, "RoomNotFound", ct);
        if (await store.Query<Device>().AnyAsync(d => d.RoomId == id, ct))
        {
            throw new ConflictException("RoomHasDevices", "The room still has devices assigned.");
        }

        if (await store.Query<TimetableEntry>().AnyAsync(e => e.RoomId == id, ct))
        {
            throw new ConflictException("RoomHasTimetable", "The room is still referenced by the timetable.");
        }

        store.Remove(room);
        await store.SaveChangesAsync(ct);
    }

    // ---- Users ----

    public Task<List<User>> ListUsersAsync(CancellationToken ct = default) =>
        store.Query<User>().OrderBy(u => u.Email).ToListAsync(ct);

    public async Task<User> CreateUserAsync(string email, string fullName, UserRole role, string password, CancellationToken ct = default)
    {
        var normalized = ValidateEmail(email);
        Require(fullName, "FullName");
        ValidateRole(role);
        ValidatePassword(password);
        if (await store.Query<User>().AnyAsync(u => u.Email == normalized, ct))
        {
            throw new ConflictException("DuplicateEmail", $"A user with email {normalized} already exists.");
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = normalized,
            FullName = fullName.Trim(),
            Role = role,
            PasswordHash = AuthService.HashPassword(password),
            IsActive = true,
            CreatedAt = time.GetUtcNow(),
        };
        store.Add(user);
        await store.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> UpdateUserAsync(
        Guid id, string email, string fullName, UserRole role, bool isActive, string? newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync<User>(u => u.Id == id, "UserNotFound", ct);
        var normalized = ValidateEmail(email);
        Require(fullName, "FullName");
        ValidateRole(role);
        if (await store.Query<User>().AnyAsync(u => u.Email == normalized && u.Id != id, ct))
        {
            throw new ConflictException("DuplicateEmail", $"A user with email {normalized} already exists.");
        }

        user.Email = normalized;
        user.FullName = fullName.Trim();
        user.Role = role;
        user.IsActive = isActive;
        if (!string.IsNullOrEmpty(newPassword))
        {
            ValidatePassword(newPassword);
            user.PasswordHash = AuthService.HashPassword(newPassword);
        }

        await store.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>
    /// Users referenced by timetable, grants or audit are deactivated rather than removed,
    /// so accountability records (FR-F5) keep resolving to a person.
    /// </summary>
    public async Task<UserDeletion> DeleteUserAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        if (actor.UserId == id)
        {
            throw new ConflictException("CannotDeleteSelf", "Administrators cannot delete their own account.");
        }

        var user = await FindAsync<User>(u => u.Id == id, "UserNotFound", ct);
        var referenced =
            await store.Query<TimetableEntry>().AnyAsync(e => e.LecturerId == id, ct)
            || await store.Query<AccessGrant>().AnyAsync(g => g.SubjectUserId == id, ct)
            || await store.Query<AuditEntry>().AnyAsync(a => a.ActorUserId == id, ct);

        if (referenced)
        {
            user.IsActive = false;
            await store.SaveChangesAsync(ct);
            return new UserDeletion(Deleted: false, Deactivated: true);
        }

        foreach (var notification in await store.Query<Notification>().Where(n => n.RecipientUserId == id).ToListAsync(ct))
        {
            store.Remove(notification);
        }

        store.Remove(user);
        await store.SaveChangesAsync(ct);
        return new UserDeletion(Deleted: true, Deactivated: false);
    }

    private async Task<T> FindAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate, string code, CancellationToken ct)
        where T : class =>
        await store.Query<T>().Where(predicate).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(code, $"{typeof(T).Name} not found.");

    private static string NormalizeCode(string code)
    {
        Require(code, "Code");
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > 32)
        {
            throw new ValidationException("InvalidCode", "Codes are at most 32 characters.");
        }

        return normalized;
    }

    private static string ValidateEmail(string email)
    {
        Require(email, "Email");
        var normalized = AuthService.NormalizeEmail(email);
        if (!MailAddress.TryCreate(normalized, out var parsed) || parsed.Address != normalized)
        {
            throw new ValidationException("InvalidEmail", "Email address is not valid.");
        }

        return normalized;
    }

    private static void ValidateRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ValidationException("InvalidRole", "Unknown role.");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            throw new ValidationException("WeakPassword", "Passwords must be at least 8 characters.");
        }
    }

    private static void Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException("Required", $"{field} is required.");
        }
    }
}
