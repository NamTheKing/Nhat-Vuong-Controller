namespace NhatVuong.Domain.Entities;

/// <summary>
/// Server-side projection of what the device last reported from its control board (AD-4).
/// Written only by device reports and command acknowledgements, never from the command that was sent.
/// </summary>
public class DeviceState
{
    public Guid DeviceId { get; set; }

    public PowerState Power { get; set; }
    public double Setpoint { get; set; } = 26;
    public AcMode Mode { get; set; }
    public FanSpeed Fan { get; set; }
    public double? RoomTemperature { get; set; }
    public bool CompressorRunning { get; set; }
    public string? ActiveErrorCode { get; set; }

    /// <summary>When the board values above were observed by the device. Null until the first report.</summary>
    public DateTimeOffset? ObservedAt { get; set; }

    public bool IsConnected { get; set; }
    public DateTimeOffset? ConnectivityChangedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Start of the current continuous run; drives the long-run alert (FR-D4).</summary>
    public DateTimeOffset? OnSince { get; set; }
    public bool LongRunAlerted { get; set; }
    public bool DisconnectAlerted { get; set; }
    public DateTimeOffset? LastSystemOffAttemptAt { get; set; }

    /// <summary>LAN endpoint and TLS certificate thumbprint the module advertises for direct control (US-24).</summary>
    public string? LanEndpoint { get; set; }
    public string? LanCertThumbprint { get; set; }

    /// <summary>A projection older than the freshness bound is unknown, not current (AD-4).</summary>
    public bool IsFresh(DateTimeOffset now, TimeSpan freshness) =>
        IsConnected && LastSeenAt is { } seen && now - seen <= freshness;

    public Connectivity GetConnectivity(DateTimeOffset now, TimeSpan freshness)
    {
        if (!IsFresh(now, freshness))
        {
            return Connectivity.Offline;
        }

        return string.IsNullOrEmpty(ActiveErrorCode) ? Connectivity.Online : Connectivity.Fault;
    }
}
