namespace NhatVuong.Contracts.Mqtt;

/// <summary>
/// MQTT topic namespace (F-8). A module may publish only to its own <c>ack/state/event/status</c> and subscribe only
/// to its own <c>cmd/config</c>; the broker enforces this per device identity.
/// </summary>
public static class MqttTopics
{
    public const string Root = "nvc/v1/devices";

    public const string CommandChannel = "cmd";
    public const string ConfigChannel = "config";
    public const string AckChannel = "ack";
    public const string StateChannel = "state";
    public const string EventChannel = "event";
    public const string StatusChannel = "status";

    public static readonly IReadOnlySet<string> DevicePublishChannels = new HashSet<string> { AckChannel, StateChannel, EventChannel, StatusChannel };
    public static readonly IReadOnlySet<string> DeviceSubscribeChannels = new HashSet<string> { CommandChannel, ConfigChannel };

    public static string For(string hardwareId, string channel) => $"{Root}/{hardwareId}/{channel}";

    public static string AllDevices(string channel) => $"{Root}/+/{channel}";

    public static bool TryParse(string topic, out string hardwareId, out string channel)
    {
        hardwareId = channel = string.Empty;
        if (!topic.StartsWith(Root + "/", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = topic[(Root.Length + 1)..].Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        hardwareId = parts[0];
        channel = parts[1];
        return true;
    }
}

/// <summary>
/// Envelope version rule (F-25): receivers process messages whose <c>Version</c> is at most <see cref="Current"/>,
/// ignore unknown fields, and drop (and log) messages from a newer version.
/// </summary>
public static class EnvelopeVersion
{
    public const int Current = 1;

    public static bool IsSupported(int version) => version is >= 1 and <= Current;
}

public sealed record BoardStateDto(
    PowerState Power,
    double Setpoint,
    AcMode Mode,
    FanSpeed Fan,
    double? RoomTemperature,
    bool CompressorRunning,
    string? ErrorCode);

/// <summary>Server → module, QoS 1, topic <c>cmd</c>.</summary>
public sealed record CommandMessage(int Version, Guid CommandId, string DeviceId, DateTimeOffset OccurredAt, CommandAction Action, string? Value);

/// <summary>Module → server, QoS 1, topic <c>ack</c>. Carries the board state read after executing (AD-4).</summary>
public sealed record AckMessage(
    int Version, Guid CommandId, string DeviceId, DateTimeOffset OccurredAt, bool Success, string? Error, BoardStateDto? State);

/// <summary>Module → server, QoS 1, topic <c>state</c>: periodic and on-change board reports.</summary>
public sealed record StateMessage(
    int Version, Guid EventId, string DeviceId, DateTimeOffset OccurredAt, BoardStateDto State, string? LanEndpoint, string? LanCertThumbprint);

/// <summary>Module → server, retained, topic <c>status</c>; the offline form is also the MQTT last will.</summary>
public sealed record StatusMessage(int Version, string DeviceId, DateTimeOffset OccurredAt, bool Online);

public static class EventKinds
{
    public const string Error = "error";
    public const string OfflineFacts = "offline-facts";
}

public static class FactSources
{
    public const string Lan = "lan";
    public const string Schedule = "schedule";
}

/// <summary>An action a module took while it could not reach the server (AD-5): reported as a fact, never re-executed.</summary>
public sealed record OfflineFactDto(
    Guid EventId,
    DateTimeOffset OccurredAt,
    CommandAction Action,
    string? Value,
    bool Success,
    string Source,
    Guid? GrantId,
    Guid? SubjectUserId,
    string? SubjectName,
    Guid? ScheduleActionId);

/// <summary>Module → server, QoS 1, topic <c>event</c>: error codes (FR-E1) and offline fact replay (US-24-2, US-25-2).</summary>
public sealed record EventMessage(
    int Version,
    Guid EventId,
    string DeviceId,
    DateTimeOffset OccurredAt,
    string Kind,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<OfflineFactDto>? Facts);

public sealed record CachedActionDto(Guid ActionId, DateTimeOffset At, CommandAction Action, string Reason);

/// <summary>Server → module, retained, topic <c>config</c>: absolute times and bounds only, never policy (AD-12).</summary>
public sealed record ConfigMessage(
    int Version,
    DateTimeOffset ServerTime,
    string ServerPublicKey,
    int OfflineThresholdSeconds,
    IReadOnlyList<CachedActionDto> Schedule,
    IReadOnlyList<Guid> RevokedGrantIds);
