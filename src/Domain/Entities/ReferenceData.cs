namespace NhatVuong.Domain.Entities;

public class Building
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public List<Room> Rooms { get; set; } = [];
}

public class Room
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public Building? Building { get; set; }

    /// <summary>Campus-wide unique code; the timetable file refers to rooms by it.</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public List<Device> Devices { get; set; } = [];
}

public class User
{
    public Guid Id { get; set; }

    /// <summary>University email, stored lower-case; the sign-in identifier (US-01).</summary>
    public required string Email { get; set; }
    public required string FullName { get; set; }

    /// <summary>BCrypt hash (NFR-03). Never a plaintext password.</summary>
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public class Device
{
    public Guid Id { get; set; }

    /// <summary>Immutable hardware id read from the module's QR code; also its MQTT username and client id.</summary>
    public required string HardwareId { get; set; }
    public required string Name { get; set; }
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    /// <summary>BCrypt hash of the per-device MQTT password (NFR-04 per-device authentication).</summary>
    public required string MqttPasswordHash { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public Guid? RegisteredBy { get; set; }
    public DeviceState? State { get; set; }
}
