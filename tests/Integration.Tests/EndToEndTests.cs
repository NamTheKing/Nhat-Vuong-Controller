using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MQTTnet;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;
using NhatVuong.Contracts.Lan;
using NhatVuong.Simulator;

namespace NhatVuong.Integration.Tests;

/// <summary>
/// App → server → MQTT → simulated module round trips. One server per test class; tests share the seeded campus
/// (class now in A101 for giangvien1, a temporary grant for loptruong in A101).
/// </summary>
public sealed class EndToEndTests : IClassFixture<NvcAppFactory>, IAsyncLifetime
{
    private readonly NvcAppFactory _app;

    public EndToEndTests(NvcAppFactory app) => _app = app;

    public Task InitializeAsync() => _app.AlwaysOpenAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "US-06-1 / US-08-1 / NFR-01: switch on over real MQTT; the app shows the board's state")]
    public async Task SwitchOnRoundTrip()
    {
        await using var device = await _app.StartDeviceAsync("SIM-A101-1");
        using var lecturer = await _app.SignInAsync("giangvien1@nhatvuong.edu.vn");
        var dto = await NvcAppFactory.WaitForAsync(lecturer, "SIM-A101-1", d => d.Connectivity == Connectivity.Online, TimeSpan.FromSeconds(10));
        Assert.True(dto.CanControlNow);

        var watch = Stopwatch.StartNew();
        var outcome = await SendAsync(lecturer, dto.Id, CommandAction.PowerOn);
        watch.Stop();

        Assert.Equal(CommandResult.Succeeded, outcome.Result);
        Assert.Equal(PowerState.On, outcome.State!.Power);
        Assert.Equal(PowerState.On, device.Power);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"round trip took {watch.Elapsed}");

        var temp = await SendAsync(lecturer, dto.Id, CommandAction.SetTemperature, "24");
        Assert.Equal(24, temp.State!.Setpoint);
        Assert.Equal(24, device.Setpoint);
    }

    [Fact(DisplayName = "US-09: a physical-remote change reaches the app within 10 seconds")]
    public async Task RemoteSync()
    {
        await using var device = await _app.StartDeviceAsync("SIM-A101-2");
        using var lecturer = await _app.SignInAsync("giangvien1@nhatvuong.edu.vn");
        await NvcAppFactory.WaitForAsync(lecturer, "SIM-A101-2", d => d.Connectivity == Connectivity.Online, TimeSpan.FromSeconds(10));

        await device.UseRemoteAsync(CommandAction.SetTemperature, "22.5");
        var synced = await NvcAppFactory.WaitForAsync(lecturer, "SIM-A101-2", d => d.State.Setpoint == 22.5, TimeSpan.FromSeconds(10));
        Assert.Equal(22.5, synced.State.Setpoint);

        await device.UseRemoteAsync(CommandAction.PowerOff, null);
        synced = await NvcAppFactory.WaitForAsync(lecturer, "SIM-A101-2", d => d.State.Power == PowerState.Off, TimeSpan.FromSeconds(10));
        Assert.Equal(PowerState.Off, synced.State.Power);
    }

    [Fact(DisplayName = "US-10-2: a device that does not answer is reported as not responding within ~5 s")]
    public async Task NoReplyTimesOut()
    {
        await using var device = await _app.StartDeviceAsync("SIM-A101-1");
        using var lecturer = await _app.SignInAsync("giangvien1@nhatvuong.edu.vn");
        var dto = await NvcAppFactory.WaitForAsync(lecturer, "SIM-A101-1", d => d.Connectivity == Connectivity.Online, TimeSpan.FromSeconds(10));
        device.NoReply = true;

        var watch = Stopwatch.StartNew();
        var outcome = await SendAsync(lecturer, dto.Id, CommandAction.SetFanSpeed, "High");

        Assert.Equal(CommandResult.Timeout, outcome.Result);
        Assert.InRange(watch.Elapsed.TotalSeconds, 4.5, 8);
    }

    [Fact(DisplayName = "NFR-05: forged and missing tokens are refused; a valid user without rights is rejected and audited")]
    public async Task ServerSideAuthorisation()
    {
        await using var device = await _app.StartDeviceAsync("SIM-A101-1");
        using var lecturer2 = await _app.SignInAsync("giangvien2@nhatvuong.edu.vn");
        using var admin = await _app.SignInAsync("admin@nhatvuong.edu.vn");
        var target = await NvcAppFactory.DeviceAsync(admin, "SIM-A101-1");
        var before = device.CommandsExecuted;

        using var anonymous = _app.CreateClient();
        var noToken = await anonymous.PostAsJsonAsync($"{ApiRoutes.Prefix}/devices/{target.Id}/commands", new SendCommandRequest(CommandAction.PowerOff, null), NvcJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);

        using var forged = _app.CreateClient();
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ForgedToken());
        var forgedResponse = await forged.PostAsJsonAsync($"{ApiRoutes.Prefix}/devices/{target.Id}/commands", new SendCommandRequest(CommandAction.PowerOff, null), NvcJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);

        var outcome = await SendAsync(lecturer2, target.Id, CommandAction.PowerOff);
        Assert.Equal(RejectionReason.NoAccess, outcome.Rejection);
        Assert.Equal(before, device.CommandsExecuted);

        var audit = await admin.GetFromJsonAsync<PagedDto<AuditEntryDto>>($"{ApiRoutes.Prefix}/audit?result=Rejected&pageSize=200", NvcJson.Options);
        Assert.Contains(audit!.Items, a => a.CommandId == outcome.CommandId && a.ActorName.Contains("Bình"));

        var adminOnly = await lecturer2.GetAsync($"{ApiRoutes.Prefix}/audit");
        Assert.Equal(HttpStatusCode.Forbidden, adminOnly.StatusCode);
    }

    [Fact(DisplayName = "US-16: a device error code creates an incident and notifies maintenance")]
    public async Task ErrorCodeFlow()
    {
        await using var device = await _app.StartDeviceAsync("SIM-B201-1");
        using var maintenance = await _app.SignInAsync("baotri@nhatvuong.edu.vn");

        await device.InjectErrorAsync("E7", "Cảm biến nhiệt lỗi");

        IncidentDto? incident = null;
        for (var i = 0; i < 50 && incident is null; i++)
        {
            var incidents = await maintenance.GetFromJsonAsync<List<IncidentDto>>($"{ApiRoutes.Prefix}/incidents?status=Open", NvcJson.Options);
            incident = incidents!.FirstOrDefault(x => x.Code == "E7");
            await Task.Delay(200);
        }

        Assert.NotNull(incident);
        var unread = await maintenance.GetFromJsonAsync<UnreadCountDto>($"{ApiRoutes.Prefix}/notifications/unread-count", NvcJson.Options);
        Assert.True(unread!.Count >= 1);

        var resolve = await maintenance.PostAsJsonAsync($"{ApiRoutes.Prefix}/incidents/{incident.Id}/resolve", new ResolveIncidentRequest("Đã thay cảm biến"), NvcJson.Options);
        Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);
    }

    [Fact(DisplayName = "US-24: a signed LAN command executes on the module without the server, then syncs upward as a fact")]
    public async Task LanControl()
    {
        VirtualDevice? device = null;
        await using var lan = await LanServer.StartAsync(hw => hw == "SIM-A102-1" ? device : null, FreePort(), "localhost");
        device = await _app.StartDeviceAsync("SIM-A102-1", lan.EndpointFor("SIM-A102-1"), lan.Thumbprint);
        await using var _ = device;

        using var admin = await _app.SignInAsync("admin@nhatvuong.edu.vn");
        var dto = await NvcAppFactory.WaitForAsync(admin, "SIM-A102-1", d => d.LanAvailable, TimeSpan.FromSeconds(10));
        var users = await admin.GetFromJsonAsync<List<UserDto>>($"{ApiRoutes.Prefix}/users", NvcJson.Options);
        var lecturer2 = users!.Single(u => u.Email == "giangvien2@nhatvuong.edu.vn");
        var grantResponse = await admin.PostAsJsonAsync($"{ApiRoutes.Prefix}/grants",
            new CreateGrantRequest(lecturer2.Id, dto.RoomId, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1), "LAN test"), NvcJson.Options);
        grantResponse.EnsureSuccessStatusCode();

        using var client = await _app.SignInAsync("giangvien2@nhatvuong.edu.vn");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issued = await (await client.PostAsJsonAsync($"{ApiRoutes.Prefix}/devices/{dto.Id}/lan-grant",
            new LanGrantRequest(LanSigning.ExportPublicKey(key)), NvcJson.Options)).Content.ReadFromJsonAsync<LanGrantResponse>(NvcJson.Options);
        Assert.NotNull(issued!.LanEndpoint);

        // Straight to the module over HTTPS, pinning the thumbprint the server relayed.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && Convert.ToHexString(SHA256.HashData(cert.RawData)) == issued.LanCertThumbprint,
        };
        using var direct = new HttpClient(handler);
        var commandId = Guid.CreateVersion7();
        var issuedAt = DateTimeOffset.UtcNow;
        var signature = LanSigning.SignCommand(key, "SIM-A102-1", commandId, CommandAction.SetTemperature, "25.0", issuedAt);
        var request = new LanCommandRequest(issued.Grant, commandId, CommandAction.SetTemperature, "25.0", issuedAt, signature);
        var lanResponse = await (await direct.PostAsJsonAsync(issued.LanEndpoint, request, NvcJson.Options)).Content.ReadFromJsonAsync<LanCommandResponse>(NvcJson.Options);
        Assert.True(lanResponse!.Success, lanResponse.Error);
        Assert.Equal(25, device.Setpoint);

        // A replayed or tampered request is refused.
        var tampered = request with { Value = "16.0" };
        var refused = await (await direct.PostAsJsonAsync(issued.LanEndpoint, tampered, NvcJson.Options)).Content.ReadFromJsonAsync<LanCommandResponse>(NvcJson.Options);
        Assert.Equal(LanErrors.BadSignature, refused!.Error);

        // AD-5: the module reports the fact; the server audits it with the LAN source.
        AuditEntryDto? fact = null;
        for (var i = 0; i < 50 && fact is null; i++)
        {
            var audit = await admin.GetFromJsonAsync<PagedDto<AuditEntryDto>>($"{ApiRoutes.Prefix}/audit?deviceId={dto.Id}&pageSize=200", NvcJson.Options);
            fact = audit!.Items.FirstOrDefault(a => a.CommandId == commandId);
            await Task.Delay(200);
        }

        Assert.NotNull(fact);
        Assert.Equal(CommandSource.Lan, fact.Source);
        Assert.Equal(lecturer2.Id, fact.ActorUserId);
    }

    [Fact(DisplayName = "US-20: timetable upload over HTTP — errors by row with nothing written, then a valid file")]
    public async Task TimetableUpload()
    {
        using var admin = await _app.SignInAsync("admin@nhatvuong.edu.vn");
        var bad = "timetable_id,room_code,lecturer_email,starts_at,ends_at\nX1,A101,giangvien1@nhatvuong.edu.vn,2027-01-04 07:30,2027-01-04 09:30\nX2,NOPE,giangvien1@nhatvuong.edu.vn,2027-01-04 10:00,2027-01-04 11:00\n";
        var badResponse = await admin.PostAsync($"{ApiRoutes.Prefix}/timetable/import", Upload(bad));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badResponse.StatusCode);
        var badResult = await badResponse.Content.ReadFromJsonAsync<TimetableImportResultDto>(NvcJson.Options);
        Assert.Equal(3, Assert.Single(badResult!.Errors).Row);

        var good = bad.Replace("NOPE", "A102");
        var goodResponse = await admin.PostAsync($"{ApiRoutes.Prefix}/timetable/import", Upload(good));
        Assert.Equal(HttpStatusCode.OK, goodResponse.StatusCode);
        Assert.Equal(2, (await goodResponse.Content.ReadFromJsonAsync<TimetableImportResultDto>(NvcJson.Options))!.ImportedCount);
    }

    [Fact(DisplayName = "US-11: room-wide command reports per-unit results")]
    public async Task RoomWide()
    {
        await using var left = await _app.StartDeviceAsync("SIM-A101-1");
        await using var right = await _app.StartDeviceAsync("SIM-A101-2");
        using var lecturer = await _app.SignInAsync("giangvien1@nhatvuong.edu.vn");
        var dto = await NvcAppFactory.WaitForAsync(lecturer, "SIM-A101-2", d => d.Connectivity == Connectivity.Online, TimeSpan.FromSeconds(10));
        right.NoReply = true;

        var response = await lecturer.PostAsJsonAsync($"{ApiRoutes.Prefix}/rooms/{dto.RoomId}/commands", new SendCommandRequest(CommandAction.SetMode, "Dry"), NvcJson.Options);
        var result = await response.Content.ReadFromJsonAsync<RoomCommandResultDto>(NvcJson.Options);

        Assert.Equal(2, result!.Total);
        Assert.Equal(1, result.Succeeded);
        Assert.Contains(result.Items, i => i.Result == CommandResult.Timeout);
        Assert.Equal(AcMode.Dry, left.Mode);
    }

    [Fact(DisplayName = "NFR-04: the broker refuses bad device credentials and confines a device to its own topics")]
    public async Task BrokerSecurity()
    {
        var badPassword = new MqttClientFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _app.MqttPort).WithClientId("SIM-A101-1").WithCredentials("SIM-A101-1", "wrong").Build();
        _ = _app.Server;
        Assert.False(await TryConnectAsync(badPassword, options));

        var impostor = new MqttClientFactory().CreateMqttClient();
        var mismatched = new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _app.MqttPort).WithClientId("SIM-A101-2").WithCredentials("SIM-A101-1", NvcAppFactory.DevicePassword).Build();
        Assert.False(await TryConnectAsync(impostor, mismatched));

        await using var victim = await _app.StartDeviceAsync("SIM-B201-1");
        using var admin = await _app.SignInAsync("admin@nhatvuong.edu.vn");
        var victimDto = await NvcAppFactory.WaitForAsync(admin, "SIM-B201-1", d => d.Connectivity == Connectivity.Online, TimeSpan.FromSeconds(10));

        // A legitimate module publishing a forged report for another module is dropped by the broker ACL.
        var attacker = new MqttClientFactory().CreateMqttClient();
        await attacker.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _app.MqttPort).WithClientId("SIM-A102-1").WithCredentials("SIM-A102-1", NvcAppFactory.DevicePassword).Build());
        var forged = new Contracts.Mqtt.StateMessage(1, Guid.NewGuid(), "SIM-B201-1", DateTimeOffset.UtcNow.AddMinutes(1),
            new Contracts.Mqtt.BoardStateDto(PowerState.On, 16, AcMode.Cool, FanSpeed.High, 20, true, null), null, null);
        await attacker.PublishBinaryAsync(Contracts.Mqtt.MqttTopics.For("SIM-B201-1", "state"), NvcJson.Serialize(forged));
        await Task.Delay(1500);
        var after = await NvcAppFactory.DeviceAsync(admin, "SIM-B201-1");
        Assert.NotEqual(16, after.State.Setpoint);
        await attacker.DisconnectAsync();
        Assert.Equal(victimDto.Id, after.Id);
    }

    [Fact(DisplayName = "CAP-23: readiness reports database, MQTT and scheduler health")]
    public async Task Health()
    {
        using var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("mqtt", await ready.Content.ReadAsStringAsync());
    }

    private static async Task<CommandOutcomeDto> SendAsync(HttpClient client, Guid deviceId, CommandAction action, string? value = null)
    {
        var response = await client.PostAsJsonAsync($"{ApiRoutes.Prefix}/devices/{deviceId}/commands", new SendCommandRequest(action, value), NvcJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommandOutcomeDto>(NvcJson.Options))!;
    }

    /// <summary>MQTTnet 5 reports a refused CONNECT through the result code (or an exception, depending on the reason).</summary>
    private static async Task<bool> TryConnectAsync(IMqttClient client, MqttClientOptions options)
    {
        try
        {
            var result = await client.ConnectAsync(options);
            return result.ResultCode == MqttClientConnectResultCode.Success && client.IsConnected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static MultipartFormDataContent Upload(string csv)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "tkb.csv");
        return content;
    }

    private static string ForgedToken()
    {
        static string B64(string s) => Base64Url.Encode(Encoding.UTF8.GetBytes(s));
        var header = B64("""{"alg":"HS256","typ":"JWT"}""");
        var payload = B64($$"""{"sub":"{{Guid.NewGuid()}}","name":"Mallory","role":"Administrator","iss":"nhatvuong-controller","aud":"nhatvuong-clients","exp":4102444800}""");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("not-the-server-key-not-the-server-key"));
        var signature = Base64Url.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
