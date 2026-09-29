namespace NhatVuong.Adapters.Mqtt;

public sealed class MqttOptions
{
    public const string Section = "Mqtt";

    /// <summary>
    /// Host the broker in the server process. This lets the broker authenticate each module against the device
    /// registry (NFR-04 per-device credentials) without a separate password file. Set false to use an external broker.
    /// </summary>
    public bool EmbeddedBroker { get; set; } = true;

    /// <summary>Broker host the server's own client connects to.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Plain-TCP port. Development only; production sets <see cref="AllowPlaintext"/> false (NFR-04).</summary>
    public int Port { get; set; } = 1883;

    public bool AllowPlaintext { get; set; } = true;

    /// <summary>TLS port of the embedded broker, enabled when <see cref="CertificatePath"/> is set.</summary>
    public int TlsPort { get; set; } = 8883;

    /// <summary>Whether the server's own client connects over TLS.</summary>
    public bool UseTls { get; set; }

    /// <summary>PKCS#12 certificate for the embedded broker's TLS endpoint.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    /// <summary>Development only: accept a self-signed broker certificate.</summary>
    public bool AllowUntrustedCertificates { get; set; }

    public string ServerUsername { get; set; } = "nvc-server";

    /// <summary>Supplied from the environment/secret store; never committed.</summary>
    public string ServerPassword { get; set; } = string.Empty;

    /// <summary>Parallel ingest workers; reports from one device always go to the same worker, preserving order.</summary>
    public int IngestWorkers { get; set; } = 4;
}
