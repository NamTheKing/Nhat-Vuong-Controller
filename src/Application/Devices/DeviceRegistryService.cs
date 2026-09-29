using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NhatVuong.Application.Abstractions;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Devices;

public sealed record RegisteredDevice(Device Device, string MqttUsername, string MqttPassword);

/// <summary>
/// Remembers a digest of recently verified device passwords so a fleet reconnecting after a broker restart
/// does not pay a BCrypt verification per device per reconnect. Cleared on credential rotation.
/// </summary>
public sealed class DeviceCredentialCache
{
    private readonly ConcurrentDictionary<string, byte[]> _verified = new(StringComparer.Ordinal);

    public bool Matches(string hardwareId, string password) =>
        _verified.TryGetValue(hardwareId, out var digest) && CryptographicOperations.FixedTimeEquals(digest, Digest(password));

    public void Remember(string hardwareId, string password) => _verified[hardwareId] = Digest(password);

    public void Forget(string hardwareId) => _verified.TryRemove(hardwareId, out _);

    private static byte[] Digest(string password) => SHA256.HashData(Encoding.UTF8.GetBytes(password));
}

/// <summary>Device registration by QR code (US-19) and per-device MQTT credentials (NFR-04).</summary>
public sealed partial class DeviceRegistryService(IDataStore store, TimeProvider time, DeviceCredentialCache credentialCache)
{
    private const int DeviceHashWorkFactor = 10;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{2,63}$")]
    private static partial Regex HardwareIdPattern();

    /// <summary>Accepts <c>NVC:&lt;id&gt;</c>, <c>nvc://device/&lt;id&gt;</c> or a bare hardware id.</summary>
    public static string ParseQrCode(string? qrCode)
    {
        var raw = (qrCode ?? string.Empty).Trim();
        // Longest prefix first: "nvc://device/" also starts with "nvc:".
        foreach (var prefix in new[] { "nvc://device/", "NVC:" })
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[prefix.Length..];
                break;
            }
        }

        var hardwareId = raw.Trim().ToUpperInvariant();
        if (!HardwareIdPattern().IsMatch(hardwareId))
        {
            throw new ValidationException("InvalidQrCode", "The QR code does not contain a valid device id.");
        }

        return hardwareId;
    }

    public async Task<RegisteredDevice> RegisterAsync(Actor actor, string qrCode, Guid roomId, string? name, CancellationToken ct = default)
    {
        var hardwareId = ParseQrCode(qrCode);
        if (!await store.Query<Room>().Where(r => r.Id == roomId).AnyAsync(ct))
        {
            throw new NotFoundException("RoomNotFound", "Room not found.");
        }

        // US-19-2: a code that is already registered is reported and never creates a second record.
        if (await store.Query<Device>().Where(d => d.HardwareId == hardwareId).AnyAsync(ct))
        {
            throw new ConflictException("DuplicateDevice", $"Device {hardwareId} is already registered.");
        }

        var password = NewPassword();
        var device = new Device
        {
            Id = Guid.CreateVersion7(),
            HardwareId = hardwareId,
            Name = string.IsNullOrWhiteSpace(name) ? hardwareId : name.Trim(),
            RoomId = roomId,
            MqttPasswordHash = BCrypt.Net.BCrypt.HashPassword(password, DeviceHashWorkFactor),
            RegisteredAt = time.GetUtcNow(),
            RegisteredBy = actor.UserId,
        };
        device.State = new DeviceState { DeviceId = device.Id };

        store.Add(device);
        await store.SaveChangesAsync(ct);
        return new RegisteredDevice(device, hardwareId, password);
    }

    public async Task<Device> UpdateAsync(Guid id, string name, Guid roomId, CancellationToken ct = default)
    {
        var device = await GetAsync(id, ct);
        if (!await store.Query<Room>().Where(r => r.Id == roomId).AnyAsync(ct))
        {
            throw new NotFoundException("RoomNotFound", "Room not found.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Required", "Name is required.");
        }

        device.Name = name.Trim();
        device.RoomId = roomId;
        await store.SaveChangesAsync(ct);
        return device;
    }

    public async Task<RegisteredDevice> RotateCredentialsAsync(Guid id, CancellationToken ct = default)
    {
        var device = await GetAsync(id, ct);
        var password = NewPassword();
        device.MqttPasswordHash = BCrypt.Net.BCrypt.HashPassword(password, DeviceHashWorkFactor);
        await store.SaveChangesAsync(ct);
        credentialCache.Forget(device.HardwareId);
        return new RegisteredDevice(device, device.HardwareId, password);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var device = await GetAsync(id, ct);
        foreach (var incident in await store.Query<Incident>().Where(i => i.DeviceId == id).ToListAsync(ct))
        {
            store.Remove(incident);
        }

        foreach (var session in await store.Query<RuntimeSession>().Where(s => s.DeviceId == id).ToListAsync(ct))
        {
            store.Remove(session);
        }

        if (device.State is not null)
        {
            store.Remove(device.State);
        }

        store.Remove(device);
        await store.SaveChangesAsync(ct);
        credentialCache.Forget(device.HardwareId);
    }

    /// <summary>Broker-side authentication of a module (NFR-04). The client id must equal the hardware id.</summary>
    public async Task<bool> ValidateCredentialsAsync(string hardwareId, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(hardwareId) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (credentialCache.Matches(hardwareId, password))
        {
            return true;
        }

        var hash = await store.Query<Device>()
            .Where(d => d.HardwareId == hardwareId)
            .Select(d => d.MqttPasswordHash)
            .FirstOrDefaultAsync(ct);

        if (hash is null || !BCrypt.Net.BCrypt.Verify(password, hash))
        {
            return false;
        }

        credentialCache.Remember(hardwareId, password);
        return true;
    }

    public async Task<Device> GetAsync(Guid id, CancellationToken ct = default) =>
        await store.Query<Device>().Where(d => d.Id == id).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("DeviceNotFound", "Device not found.");

    private static string NewPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
