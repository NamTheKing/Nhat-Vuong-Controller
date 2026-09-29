using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NhatVuong.Adapters.Persistence;
using NhatVuong.Application;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Access;
using NhatVuong.Application.Identity;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;
using NhatVuong.Domain.Policies;

namespace NhatVuong.Application.Tests.Support;

/// <summary>
/// Application core wired to a real EF Core model on in-memory SQLite, a scriptable device port and a controllable
/// clock (docs/06-testing-strategy.md: "Time-dependent rules tested with a controllable clock, not real waiting").
/// </summary>
public sealed class TestHarness : IAsyncDisposable
{
    /// <summary>Monday 2026-10-05 08:00 campus time (Asia/Ho_Chi_Minh, UTC+7).</summary>
    public static readonly DateTimeOffset MondayEightLocal = new(2026, 10, 5, 1, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public TestHarness(DateTimeOffset? start = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Time = new TestClock(start ?? MondayEightLocal);
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<TimeProvider>(Time);
        services.AddNvcApplication();
        services.AddDbContext<NvcDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IDataStore, EfDataStore>();
        services.AddSingleton<IDevicePort>(Devices);
        services.AddSingleton<INotificationPort>(Notifications);
        services.AddSingleton<IGrantSigner>(Signer);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NvcDbContext>();
        db.Database.EnsureCreated();
        db.Policies.Add(new PolicySettings());
        db.SaveChanges();
    }

    public TestClock Time { get; }

    public FakeDevicePort Devices { get; } = new();

    public RecordingNotificationPort Notifications { get; } = new();

    public TestSigner Signer { get; } = new();

    public DateTimeOffset Now => Time.GetUtcNow();

    public CampusClock Clock => CampusClock.For(new PolicySettings());

    /// <summary>Campus-local wall clock on the harness's start day.</summary>
    public DateTimeOffset Local(int hour, int minute = 0, int dayOffset = 0) =>
        Clock.FromLocal(Clock.ToLocal(MondayEightLocal).Date.AddDays(dayOffset).AddHours(hour).AddMinutes(minute));

    public void SetLocalTime(int hour, int minute = 0, int dayOffset = 0) => Time.SetUtcNow(Local(hour, minute, dayOffset));

    /// <summary>A fresh scope per call, like one HTTP request or one scheduler tick.</summary>
    public T Service<T>() where T : notnull => _provider.CreateScope().ServiceProvider.GetRequiredService<T>();

    public async Task<TResult> WithDbAsync<TResult>(Func<NvcDbContext, Task<TResult>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<NvcDbContext>());
    }

    public Task WithDbAsync(Func<NvcDbContext, Task> action) => WithDbAsync(async db =>
    {
        await action(db);
        return 0;
    });

    public async Task UpdatePolicyAsync(Action<PolicySettings> change)
    {
        await WithDbAsync(async db =>
        {
            var policy = await db.Policies.SingleAsync();
            change(policy);
            await db.SaveChangesAsync();
        });
        _provider.GetRequiredService<Policy.PolicyCache>().Set(await WithDbAsync(db => db.Policies.AsNoTracking().SingleAsync()));
    }

    /// <summary>Devices report in at the current (fake) time, as they would every few seconds.</summary>
    public Task TouchDevicesAsync() => WithDbAsync(async db =>
    {
        foreach (var state in await EntityFrameworkQueryableExtensions.ToListAsync(db.DeviceStates.Where(s => s.IsConnected)))
        {
            state.LastSeenAt = Now;
        }

        await db.SaveChangesAsync();
    });

    // ---- Seed helpers ----

    public async Task<User> AddUserAsync(UserRole role, string email, string password = "Password#1", bool active = true)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            FullName = email.Split('@')[0],
            Role = role,
            PasswordHash = AuthService.HashPassword(password),
            IsActive = active,
            CreatedAt = Now,
        };
        await WithDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    public async Task<Room> AddRoomAsync(string code, string buildingCode = "A")
    {
        return await WithDbAsync(async db =>
        {
            var building = await db.Buildings.FirstOrDefaultAsync(b => b.Code == buildingCode);
            if (building is null)
            {
                building = new Building { Id = Guid.CreateVersion7(), Code = buildingCode, Name = $"Tòa {buildingCode}" };
                db.Buildings.Add(building);
            }

            var room = new Room { Id = Guid.CreateVersion7(), BuildingId = building.Id, Code = code, Name = $"Phòng {code}" };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            return room;
        });
    }

    public async Task<Device> AddDeviceAsync(Room room, string hardwareId, bool connected = true, PowerState power = PowerState.Off)
    {
        var device = new Device
        {
            Id = Guid.CreateVersion7(),
            HardwareId = hardwareId,
            Name = $"AC {hardwareId}",
            RoomId = room.Id,
            MqttPasswordHash = BCrypt.Net.BCrypt.HashPassword("device-secret", 4),
            RegisteredAt = Now,
        };
        device.State = new DeviceState
        {
            DeviceId = device.Id,
            IsConnected = connected,
            LastSeenAt = Now,
            ConnectivityChangedAt = Now,
            ObservedAt = Now,
            Power = power,
            OnSince = power == PowerState.On ? Now : null,
        };
        await WithDbAsync(async db =>
        {
            db.Devices.Add(device);
            await db.SaveChangesAsync();
        });
        Devices.Register(hardwareId, power);
        return device;
    }

    /// <summary>A class plus its materialised grant, as a timetable import would create them (AD-2).</summary>
    public async Task<TimetableEntry> AddClassAsync(Room room, User lecturer, DateTimeOffset startsAt, TimeSpan duration, string? externalId = null)
    {
        var entry = new TimetableEntry
        {
            Id = Guid.CreateVersion7(),
            ExternalId = externalId ?? $"T-{Guid.NewGuid():N}"[..12],
            RoomId = room.Id,
            LecturerId = lecturer.Id,
            StartsAt = startsAt,
            EndsAt = startsAt + duration,
        };
        await WithDbAsync(async db =>
        {
            db.TimetableEntries.Add(entry);
            db.AccessGrants.Add(AccessService.GrantFor(entry, await db.Policies.SingleAsync(), Now));
            await db.SaveChangesAsync();
        });
        return entry;
    }

    public async Task<AccessGrant> AddTemporaryGrantAsync(User user, Room room, DateTimeOffset from, DateTimeOffset to)
    {
        var grant = new AccessGrant
        {
            Id = Guid.CreateVersion7(),
            SubjectUserId = user.Id,
            RoomId = room.Id,
            ValidFrom = from,
            ValidTo = to,
            Source = GrantSource.Temporary,
            CreatedAt = Now,
        };
        await WithDbAsync(async db =>
        {
            db.AccessGrants.Add(grant);
            await db.SaveChangesAsync();
        });
        return grant;
    }

    public static Actor ActorOf(User user) => new(user.Id, user.FullName, user.Role);

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>A scriptable device fleet behind <see cref="IDevicePort"/>: executes commands like a board, or times out.</summary>
public sealed class FakeDevicePort : IDevicePort
{
    private readonly ConcurrentDictionary<string, ObservedState> _boards = new();

    public bool IsConnected { get; set; } = true;

    public ConcurrentBag<DeviceCommand> Sent { get; } = [];

    public ConcurrentDictionary<string, bool> Silent { get; } = new();

    public ConcurrentDictionary<string, DeviceConfig> Configs { get; } = new();

    public void Register(string hardwareId, PowerState power) =>
        _boards[hardwareId] = new ObservedState(power, 26, AcMode.Cool, FanSpeed.Auto, 30, power == PowerState.On, null);

    public ObservedState Board(string hardwareId) => _boards[hardwareId];

    public Task<DeviceAck> SendCommandAsync(DeviceCommand command, TimeSpan timeout, CancellationToken ct)
    {
        Sent.Add(command);
        if (Silent.GetValueOrDefault(command.HardwareId))
        {
            throw new DeviceTimeoutException("no reply");
        }

        var board = _boards.GetOrAdd(command.HardwareId, _ => new ObservedState(PowerState.Off, 26, AcMode.Cool, FanSpeed.Auto, 30, false, null));
        board = command.Action switch
        {
            CommandAction.PowerOn => board with { Power = PowerState.On, CompressorRunning = true },
            CommandAction.PowerOff => board with { Power = PowerState.Off, CompressorRunning = false },
            CommandAction.SetTemperature => board with { Setpoint = double.Parse(command.Value!, System.Globalization.CultureInfo.InvariantCulture) },
            CommandAction.SetMode => board with { Mode = Enum.Parse<AcMode>(command.Value!) },
            CommandAction.SetFanSpeed => board with { Fan = Enum.Parse<FanSpeed>(command.Value!) },
            _ => board,
        };
        _boards[command.HardwareId] = board;
        return Task.FromResult(new DeviceAck(command.CommandId, true, null, board, command.IssuedAt));
    }

    public Task PublishConfigAsync(string hardwareId, DeviceConfig config, CancellationToken ct)
    {
        Configs[hardwareId] = config;
        return Task.CompletedTask;
    }
}

public sealed class RecordingNotificationPort : INotificationPort
{
    public ConcurrentBag<(OutboundNotification Notification, IReadOnlyList<string> Recipients)> Delivered { get; } = [];

    public Task DeliverAsync(OutboundNotification notification, IReadOnlyList<User> recipients, CancellationToken ct)
    {
        Delivered.Add((notification, recipients.Select(r => r.Email).ToList()));
        return Task.CompletedTask;
    }
}

public sealed class TestSigner : IGrantSigner, IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public string Algorithm => "ES256";

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose() => _key.Dispose();
}

/// <summary>A settable clock. Unlike FakeTimeProvider it may move backwards, so a test can start at 08:00 and then look at 07:46.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void SetUtcNow(DateTimeOffset value) => _now = value.ToUniversalTime();

    public void Advance(TimeSpan delta) => _now += delta;
}
