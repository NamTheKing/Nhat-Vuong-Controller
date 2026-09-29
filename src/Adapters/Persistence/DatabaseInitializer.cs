using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NhatVuong.Application.Access;
using NhatVuong.Application.Identity;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Adapters.Persistence;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    /// <summary>Created only when no administrator exists. Supply the password from the environment in production.</summary>
    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    /// <summary>Development only: sample campus, users, simulator devices and a timetable around today.</summary>
    public bool DemoData { get; set; }

    public string DemoPassword { get; set; } = "Demo@12345";

    public string DemoDevicePassword { get; set; } = "sim-device-secret";
}

public sealed class DatabaseInitializer(NvcDbContext db, TimeProvider time, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(SeedOptions seed, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (!await db.Policies.AnyAsync(ct))
        {
            db.Policies.Add(new PolicySettings());
            await db.SaveChangesAsync(ct);
        }

        if (seed.DemoData && !await db.Users.AnyAsync(ct))
        {
            await SeedDemoAsync(seed, ct);
        }

        if (!string.IsNullOrWhiteSpace(seed.AdminEmail) && !string.IsNullOrEmpty(seed.AdminPassword)
            && !await db.Users.AnyAsync(u => u.Role == UserRole.Administrator, ct))
        {
            db.Users.Add(new User
            {
                Id = Guid.CreateVersion7(),
                Email = AuthService.NormalizeEmail(seed.AdminEmail),
                FullName = "Quản trị viên",
                Role = UserRole.Administrator,
                PasswordHash = AuthService.HashPassword(seed.AdminPassword),
                CreatedAt = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Bootstrap administrator {Email} created", seed.AdminEmail);
        }
    }

    private async Task SeedDemoAsync(SeedOptions seed, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var policy = await db.Policies.SingleAsync(ct);
        var clock = CampusClock.For(policy);
        var passwordHash = AuthService.HashPassword(seed.DemoPassword);
        var deviceHash = BCrypt.Net.BCrypt.HashPassword(seed.DemoDevicePassword, 10);

        User NewUser(string email, string name, UserRole role) => new()
        {
            Id = Guid.CreateVersion7(), Email = email, FullName = name, Role = role, PasswordHash = passwordHash, CreatedAt = now,
        };

        var admin = NewUser("admin@nhatvuong.edu.vn", "Quản trị viên", UserRole.Administrator);
        var lecturer1 = NewUser("giangvien1@nhatvuong.edu.vn", "TS. Nguyễn Văn An", UserRole.Lecturer);
        var lecturer2 = NewUser("giangvien2@nhatvuong.edu.vn", "ThS. Trần Thị Bình", UserRole.Lecturer);
        var monitor = NewUser("loptruong@nhatvuong.edu.vn", "Lê Văn Cường (lớp trưởng)", UserRole.ClassMonitor);
        var maintenance = NewUser("baotri@nhatvuong.edu.vn", "Phạm Thị Dung (bảo trì)", UserRole.MaintenanceStaff);
        db.Users.AddRange(admin, lecturer1, lecturer2, monitor, maintenance);

        var buildingA = new Building { Id = Guid.CreateVersion7(), Code = "A", Name = "Tòa A" };
        var buildingB = new Building { Id = Guid.CreateVersion7(), Code = "B", Name = "Tòa B" };
        var a101 = new Room { Id = Guid.CreateVersion7(), BuildingId = buildingA.Id, Code = "A101", Name = "Phòng A101" };
        var a102 = new Room { Id = Guid.CreateVersion7(), BuildingId = buildingA.Id, Code = "A102", Name = "Phòng A102" };
        var b201 = new Room { Id = Guid.CreateVersion7(), BuildingId = buildingB.Id, Code = "B201", Name = "Phòng B201" };
        db.Buildings.AddRange(buildingA, buildingB);
        db.Rooms.AddRange(a101, a102, b201);

        Device NewDevice(string hardwareId, string name, Room room)
        {
            var device = new Device
            {
                Id = Guid.CreateVersion7(), HardwareId = hardwareId, Name = name, RoomId = room.Id,
                MqttPasswordHash = deviceHash, RegisteredAt = now, RegisteredBy = admin.Id,
            };
            device.State = new DeviceState { DeviceId = device.Id };
            return device;
        }

        db.Devices.AddRange(
            NewDevice("SIM-A101-1", "Điều hòa A101 - trái", a101),
            NewDevice("SIM-A101-2", "Điều hòa A101 - phải", a101),
            NewDevice("SIM-A102-1", "Điều hòa A102", a102),
            NewDevice("SIM-B201-1", "Điều hòa B201", b201));

        // A class running now in A101, one later today in A102, one tomorrow in B201 (campus local time).
        var local = clock.ToLocal(now);
        var slotStart = local.Date.AddHours(local.Hour);
        TimetableEntry Class(string id, Room room, User lecturer, DateTime start, int minutes) => new()
        {
            Id = Guid.CreateVersion7(), ExternalId = id, RoomId = room.Id, LecturerId = lecturer.Id,
            StartsAt = clock.FromLocal(start), EndsAt = clock.FromLocal(start.AddMinutes(minutes)), ImportBatchId = Guid.Empty,
        };

        var entries = new[]
        {
            Class("DEMO-001", a101, lecturer1, slotStart, 120),
            Class("DEMO-002", a102, lecturer2, slotStart.AddHours(3), 90),
            Class("DEMO-003", b201, lecturer1, slotStart.AddDays(1), 90),
        };
        db.TimetableEntries.AddRange(entries);
        db.AccessGrants.AddRange(entries.Select(e => AccessService.GrantFor(e, policy, now)));

        // The class monitor gets rights in A101 for today by temporary grant (US-04), to demonstrate precedence (US-07).
        db.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.CreateVersion7(),
            SubjectUserId = monitor.Id,
            RoomId = a101.Id,
            ValidFrom = now.AddMinutes(-5),
            ValidTo = clock.FromLocal(local.Date.AddDays(1)),
            Source = GrantSource.Temporary,
            CreatedBy = admin.Id,
            CreatedAt = now,
            Note = "Demo: lớp trưởng được cấp quyền tạm thời",
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo data seeded: 5 users, 3 rooms, 4 simulator devices, 3 classes");
    }
}
