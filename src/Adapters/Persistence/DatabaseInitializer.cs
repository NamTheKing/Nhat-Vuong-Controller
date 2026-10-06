using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NhatVuong.Application;
using NhatVuong.Application.Access;
using NhatVuong.Application.Identity;
using NhatVuong.Application.Maintenance;
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

        // Separate from the step above so a development database created before this step existed gets it too.
        if (seed.DemoData && !await db.Incidents.AnyAsync(ct))
        {
            await SeedDemoActivityAsync(ct);
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

    /// <summary>
    /// Development only: incident history and the notifications it would have produced, so the maintenance,
    /// administrator and class-monitor screens are not empty in a demo. Rows and texts match what
    /// MaintenanceService and CommandService write when the events happen for real.
    /// </summary>
    private async Task SeedDemoActivityAsync(CancellationToken ct)
    {
        var users = await db.Users.ToDictionaryAsync(u => u.Email, ct);
        var devices = await db.Devices.Include(d => d.Room).ToDictionaryAsync(d => d.HardwareId, ct);
        if (!users.TryGetValue("baotri@nhatvuong.edu.vn", out var maintenance)
            || !users.TryGetValue("admin@nhatvuong.edu.vn", out var admin)
            || !users.TryGetValue("giangvien1@nhatvuong.edu.vn", out var lecturer)
            || !users.TryGetValue("loptruong@nhatvuong.edu.vn", out var monitor)
            || !devices.TryGetValue("SIM-A101-1", out var a101Left)
            || !devices.TryGetValue("SIM-A101-2", out var a101Right)
            || !devices.TryGetValue("SIM-A102-1", out var a102)
            || !devices.TryGetValue("SIM-B201-1", out var b201))
        {
            return; // not the demo campus
        }

        var now = time.GetUtcNow();
        var policy = await db.Policies.SingleAsync(ct);

        Incident NewIncident(Device device, IncidentKind kind, string code, string? message, TimeSpan ago, int count = 1) => new()
        {
            Id = Guid.CreateVersion7(), DeviceId = device.Id, Kind = kind, Code = code, Message = message,
            OccurredAt = now - ago, LastOccurredAt = now - ago / count, OccurrenceCount = count, Status = IncidentStatus.Open,
        };

        void Resolve(Incident incident, TimeSpan ago, string note)
        {
            incident.Status = IncidentStatus.Resolved;
            incident.ResolvedBy = maintenance.Id;
            incident.ResolvedByName = maintenance.FullName;
            incident.ResolvedAt = now - ago;
            incident.ResolutionNote = note;
        }

        void Notify(User recipient, string category, string title, string body, DateTimeOffset at, bool read, Incident? incident = null) =>
            db.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(), RecipientUserId = recipient.Id, Category = category, Title = title, Body = body,
                IncidentId = incident?.Id, CreatedAt = at, ReadAt = read ? Min(at.AddMinutes(20), now) : null,
            });

        static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

        void NotifyIncident(Incident incident, Device device, bool read, params User[] recipients)
        {
            var room = device.Room?.Code ?? "?";
            var (category, title, body) = incident.Kind switch
            {
                IncidentKind.DeviceError => (Text.Get("Notify_Category_Incident"), Text.Get("Notify_DeviceError_Title", incident.Code),
                    Text.Get("Notify_DeviceError_Body", device.Name, room, incident.Code, incident.Message ?? string.Empty)),
                IncidentKind.ProlongedDisconnect => (Text.Get("Notify_Category_Alert"), Text.Get("Notify_Disconnect_Title"),
                    incident.Message ?? Text.Get("Notify_Disconnect_Title")),
                _ => (Text.Get("Notify_Category_Alert"), Text.Get("Notify_LongRun_Title"), incident.Message ?? Text.Get("Notify_LongRun_Title")),
            };
            foreach (var recipient in recipients)
            {
                Notify(recipient, category, title, body, incident.OccurredAt, read, incident);
            }
        }

        // Open: a repeating sensor fault in B201, a module offline in A102, a unit left running in A101.
        var sensorFault = NewIncident(b201, IncidentKind.DeviceError, "E1", "Lỗi cảm biến nhiệt độ phòng", TimeSpan.FromHours(2), count: 3);
        var offline = NewIncident(a102, IncidentKind.ProlongedDisconnect, MaintenanceService.DisconnectCode,
            Text.Get("Notify_Disconnect_Body", a102.Name, a102.Room?.Code, 30), TimeSpan.FromMinutes(45));
        var longRun = NewIncident(a101Right, IncidentKind.LongRun, MaintenanceService.LongRunCode,
            Text.Get("Notify_LongRun_Body", a101Right.Name, a101Right.Room?.Code, policy.LongRunAlertHours), TimeSpan.FromMinutes(25));

        // Resolved history, with who fixed it and how (US-17).
        var drainFault = NewIncident(a101Left, IncidentKind.DeviceError, "E4", "Tắc ống thoát nước", TimeSpan.FromDays(3));
        Resolve(drainFault, TimeSpan.FromDays(3) - TimeSpan.FromHours(2), "Đã thông ống thoát nước và vệ sinh máng hứng nước. Máy chạy bình thường.");
        var oldOffline = NewIncident(b201, IncidentKind.ProlongedDisconnect, MaintenanceService.DisconnectCode,
            Text.Get("Notify_Disconnect_Body", b201.Name, b201.Room?.Code, 30), TimeSpan.FromDays(1));
        Resolve(oldOffline, TimeSpan.FromDays(1) - TimeSpan.FromHours(1), "Ổ cắm của mô-đun bị lỏng, đã cắm lại và cố định.");

        db.Incidents.AddRange(sensorFault, offline, longRun, drainFault, oldOffline);

        NotifyIncident(sensorFault, b201, read: false, maintenance);
        NotifyIncident(offline, a102, read: false, maintenance);
        NotifyIncident(longRun, a101Right, read: false, maintenance, admin);
        NotifyIncident(drainFault, a101Left, read: true, maintenance);
        NotifyIncident(oldOffline, b201, read: true, maintenance);

        // Lecturer precedence (US-07): the class monitor was overridden, then rejected, in A101.
        Notify(monitor, Text.Get("Notify_Category_Control"), Text.Get("Notify_Overridden_Title"),
            Text.Get("Notify_Overridden_Body", lecturer.FullName, a101Left.Name), now.AddMinutes(-15), read: true);
        Notify(monitor, Text.Get("Notify_Category_Control"), Text.Get("Notify_PrecedenceRejected_Title"),
            Text.Get("Notify_PrecedenceRejected_Body", a101Left.Name, lecturer.FullName), now.AddMinutes(-10), read: false);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo activity seeded: 5 incidents (3 open), 8 notifications");
    }
}
