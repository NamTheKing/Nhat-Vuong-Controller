using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Lan;

namespace NhatVuong.Simulator;

/// <summary>
/// The modules' LAN endpoint (US-24), multiplexed for the whole simulated fleet on one HTTPS port. A real module
/// serves the same route on its own address. The TLS certificate is self-signed; its SHA-256 thumbprint is reported
/// to the server in state messages, and the app pins it (NFR-04 on the LAN hop).
/// </summary>
public sealed class LanServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LanServer(WebApplication app, string thumbprint, string baseUrl)
    {
        _app = app;
        Thumbprint = thumbprint;
        BaseUrl = baseUrl;
    }

    public string Thumbprint { get; }

    public string BaseUrl { get; }

    public string EndpointFor(string hardwareId) => $"{BaseUrl}/lan/{hardwareId}/command";

    public static async Task<LanServer> StartAsync(Func<string, VirtualDevice?> resolve, int port, string advertisedHost)
    {
        var certificate = CreateCertificate(advertisedHost);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Any, port, listen => listen.UseHttps(certificate)));
        builder.Services.ConfigureHttpJsonOptions(o => NvcJson.Configure(o.SerializerOptions));

        var app = builder.Build();
        app.MapPost("/lan/{hardwareId}/command", async (string hardwareId, LanCommandRequest request) =>
            resolve(hardwareId) is { } device
                ? Results.Ok(await device.HandleLanCommandAsync(request))
                : Results.NotFound());
        await app.StartAsync();

        var thumbprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        return new LanServer(app, thumbprint, $"https://{advertisedHost}:{port}");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static X509Certificate2 CreateCertificate(string advertisedHost)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=nvc-module-simulator", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        if (IPAddress.TryParse(advertisedHost, out var ip))
        {
            san.AddIpAddress(ip);
        }
        else if (advertisedHost != "localhost")
        {
            san.AddDnsName(advertisedHost);
        }

        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        // Round-trip through PKCS#12 so the private key is usable by SslStream on every platform.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }
}
