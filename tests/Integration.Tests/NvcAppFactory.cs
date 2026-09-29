using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;
using NhatVuong.Simulator;

namespace NhatVuong.Integration.Tests;

/// <summary>
/// The real server (REST + embedded MQTT broker + ingest) with the development seed, on a temporary SQLite file and a
/// free TCP port. Simulated modules connect over real MQTT (docs/01-scrum-process.md: demo over real transport).
/// </summary>
public sealed class NvcAppFactory : WebApplicationFactory<Program>
{
    public const string DevicePassword = "sim-device-secret";
    public const string UserPassword = "Demo@12345";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nvc-it-" + Guid.NewGuid().ToString("N"));

    public int MqttPort { get; } = FreePort();

    public BrokerEndpoint Broker => new("127.0.0.1", MqttPort, UseTls: false, AllowUntrusted: false);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory);
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:ConnectionString", $"Data Source={Path.Combine(_directory, "it.db")}");
        builder.UseSetting("Mqtt:Port", MqttPort.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Mqtt:Host", "127.0.0.1");
        builder.UseSetting("Mqtt:AllowPlaintext", "true");
        builder.UseSetting("Mqtt:UseTls", "false");
        builder.UseSetting("Mqtt:ServerPassword", "integration-mqtt-password");
        builder.UseSetting("Auth:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "1000");
        builder.UseSetting("Signing:KeyPath", Path.Combine(_directory, "grant.pem"));
        builder.UseSetting("Seed:DemoData", "true");
        builder.UseSetting("Scheduler:Enabled", "false");
    }

    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync($"{ApiRoutes.Prefix}/auth/login", new LoginRequest(email, UserPassword), NvcJson.Options);
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(NvcJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    /// <summary>Opens the operating hours around the clock so tests do not depend on the time they run at.</summary>
    public async Task AlwaysOpenAsync()
    {
        using var admin = await SignInAsync("admin@nhatvuong.edu.vn");
        var policy = (await admin.GetFromJsonAsync<PolicyDto>($"{ApiRoutes.Prefix}/policy", NvcJson.Options))!;
        var response = await admin.PutAsJsonAsync(
            $"{ApiRoutes.Prefix}/policy", policy with { OperatingStart = new TimeOnly(0, 0), OperatingEnd = new TimeOnly(0, 0) }, NvcJson.Options);
        response.EnsureSuccessStatusCode();
    }

    public async Task<VirtualDevice> StartDeviceAsync(string hardwareId, string? lanEndpoint = null, string? lanThumbprint = null)
    {
        _ = Server; // make sure the host (and its broker) is running
        var device = new VirtualDevice(hardwareId, DevicePassword, Broker)
        {
            ReportInterval = TimeSpan.FromSeconds(1),
            LanEndpoint = lanEndpoint,
            LanCertThumbprint = lanThumbprint,
        };
        await device.StartAsync();
        Assert.True(await device.WaitUntilConnectedAsync(TimeSpan.FromSeconds(15)), $"{hardwareId} did not connect");
        return device;
    }

    public static async Task<DeviceDto> DeviceAsync(HttpClient client, string hardwareId)
    {
        var devices = await client.GetFromJsonAsync<List<DeviceDto>>($"{ApiRoutes.Prefix}/devices", NvcJson.Options);
        return devices!.Single(d => d.HardwareId == hardwareId);
    }

    public static async Task<DeviceDto> WaitForAsync(HttpClient client, string hardwareId, Func<DeviceDto, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        DeviceDto current;
        do
        {
            current = await DeviceAsync(client, hardwareId);
            if (condition(current))
            {
                return current;
            }

            await Task.Delay(200);
        }
        while (DateTime.UtcNow < deadline);

        return current;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
