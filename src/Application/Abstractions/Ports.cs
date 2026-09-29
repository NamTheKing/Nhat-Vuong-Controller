using NhatVuong.Domain;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Abstractions;

/// <summary>Board values as the device read them from the control board (AD-4).</summary>
public sealed record ObservedState(
    PowerState Power,
    double Setpoint,
    AcMode Mode,
    FanSpeed Fan,
    double? RoomTemperature,
    bool CompressorRunning,
    string? ErrorCode);

/// <summary>A single, correlated command to one device (AD-9).</summary>
public sealed record DeviceCommand(Guid CommandId, string HardwareId, CommandAction Action, string? Value, DateTimeOffset IssuedAt);

public sealed record DeviceAck(Guid CommandId, bool Success, string? Error, ObservedState? State, DateTimeOffset ObservedAt);

/// <summary>An action a module may perform on its own while it cannot reach the server (US-25, AD-6).</summary>
public sealed record CachedAction(Guid ActionId, DateTimeOffset At, CommandAction Action, string Reason);

/// <summary>Everything a module needs to act offline. Absolute times and bounds only, never policy (AD-12).</summary>
public sealed record DeviceConfig(
    DateTimeOffset ServerTime,
    string ServerPublicKey,
    int OfflineThresholdSeconds,
    IReadOnlyList<CachedAction> Schedule,
    IReadOnlyList<Guid> RevokedGrantIds);

public class DeviceUnavailableException(string message) : Exception(message);

public class DeviceTimeoutException(string message) : Exception(message);

/// <summary>Driven port to the device transport. Only <c>CommandService</c> may send commands (AD-1).</summary>
public interface IDevicePort
{
    bool IsConnected { get; }

    Task<DeviceAck> SendCommandAsync(DeviceCommand command, TimeSpan timeout, CancellationToken ct);

    Task PublishConfigAsync(string hardwareId, DeviceConfig config, CancellationToken ct);
}

public sealed record OutboundNotification(string Category, string Title, string Body, Guid? IncidentId);

/// <summary>External delivery (e-mail, push, log). In-app notifications are stored by the core regardless.</summary>
public interface INotificationPort
{
    Task DeliverAsync(OutboundNotification notification, IReadOnlyList<User> recipients, CancellationToken ct);
}

/// <summary>Signs LAN grants (AD-3). Devices hold only the public key (AD-14).</summary>
public interface IGrantSigner
{
    /// <summary>Base64 SubjectPublicKeyInfo of the signing key.</summary>
    string PublicKey { get; }

    string Algorithm { get; }

    byte[] Sign(byte[] data);
}
