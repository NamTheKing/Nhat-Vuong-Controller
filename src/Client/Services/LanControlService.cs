using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;
using NhatVuong.Contracts.Lan;

namespace NhatVuong.Client.Services;

public sealed record LanOutcome(bool Success, string? Error, Contracts.Mqtt.BoardStateDto? State);

/// <summary>
/// Direct LAN control (US-24). While the server is reachable the app obtains a server-signed grant bound to this
/// installation's key; if the server later becomes unreachable, commands go straight to the module, signed with the
/// private key (proof of possession), over HTTPS pinned to the thumbprint the server relayed. The module reports
/// what it did when it reconnects (AD-5).
/// </summary>
public sealed class LanControlService(ApiClient api)
{
    private const string KeyStorageKey = "lan_client_key";
    private readonly ConcurrentDictionary<Guid, LanGrantResponse> _grants = new();
    private ECDsa? _key;

    public LanGrantResponse? GrantFor(Guid deviceId) =>
        _grants.TryGetValue(deviceId, out var grant) && grant.ValidTo > DateTimeOffset.UtcNow.AddSeconds(30) ? grant : null;

    /// <summary>Fetches (or refreshes) a grant while online. Silently skipped when the device has no LAN endpoint.</summary>
    public async Task<LanGrantResponse?> PrepareAsync(DeviceDto device)
    {
        if (!device.LanAvailable || !device.CanControlNow)
        {
            return null;
        }

        if (GrantFor(device.Id) is { } existing && existing.ValidTo > DateTimeOffset.UtcNow.AddMinutes(10))
        {
            return existing;
        }

        var key = await GetKeyAsync();
        var grant = await api.GetLanGrantAsync(device.Id, LanSigning.ExportPublicKey(key));
        _grants[device.Id] = grant;
        return grant;
    }

    public async Task<LanOutcome> SendAsync(DeviceDto device, CommandAction action, string? value)
    {
        var grant = GrantFor(device.Id);
        if (grant?.LanEndpoint is null || grant.LanCertThumbprint is null)
        {
            return new LanOutcome(false, "no-grant", null);
        }

        var key = await GetKeyAsync();
        var commandId = Guid.CreateVersion7();
        var issuedAt = DateTimeOffset.UtcNow;
        var signature = LanSigning.SignCommand(key, device.HardwareId, commandId, action, value, issuedAt);
        var request = new LanCommandRequest(grant.Grant, commandId, action, value, issuedAt, signature);

        using var handler = new HttpClientHandler
        {
            // Pin the module's self-signed certificate to the thumbprint relayed by the server (NFR-04 on the LAN hop).
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && string.Equals(Convert.ToHexString(SHA256.HashData(cert.RawData)), grant.LanCertThumbprint, StringComparison.OrdinalIgnoreCase),
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.PostAsJsonAsync(grant.LanEndpoint, request, NvcJson.Options);
            var result = await response.Content.ReadFromJsonAsync<LanCommandResponse>(NvcJson.Options);
            return new LanOutcome(result?.Success == true, result?.Error, result?.State);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new LanOutcome(false, "unreachable", null);
        }
    }

    private async Task<ECDsa> GetKeyAsync()
    {
        if (_key is not null)
        {
            return _key;
        }

        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var stored = await SecureStorage.Default.GetAsync(KeyStorageKey);
        if (stored is not null)
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(stored), out _);
        }
        else
        {
            await SecureStorage.Default.SetAsync(KeyStorageKey, Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
        }

        return _key = key;
    }
}
