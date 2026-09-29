using System.Security.Cryptography;
using System.Text.Json;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Access;
using NhatVuong.Application.Policy;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Devices;

/// <summary>Claims of a LAN grant. Serialised with web (camelCase) JSON; the module parses the same shape.</summary>
public sealed record LanGrantClaims(
    Guid GrantId,
    Guid SubjectUserId,
    string SubjectName,
    string DeviceId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string ClientPublicKey);

public sealed record IssuedLanGrant(
    string Payload,
    string Signature,
    string Algorithm,
    DateTimeOffset ValidTo,
    string? LanEndpoint,
    string? LanCertThumbprint,
    string ServerPublicKey);

/// <summary>
/// Issues server-signed LAN grants (US-24, AD-3). The grant binds subject, device, validity and the client's own
/// public key; the client must sign every LAN command with the matching private key, so an observed grant cannot
/// be replayed by someone else. Lifetime never exceeds the underlying grant nor the configured maximum.
/// </summary>
public sealed class OfflineGrantService(
    IDataStore store,
    PolicyProvider policyProvider,
    AccessService access,
    IGrantSigner signer,
    TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IssuedLanGrant> IssueAsync(Actor actor, Guid deviceId, string clientPublicKey, CancellationToken ct = default)
    {
        var userId = actor.RequireUserId();
        var now = time.GetUtcNow();
        var policy = await policyProvider.GetAsync(ct);
        var device = await store.Query<Device>().Where(d => d.Id == deviceId).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException("DeviceNotFound", "Device not found.");
        var grant = await access.FindEffectiveGrantAsync(userId, device.RoomId, now, ct)
                    ?? throw new ForbiddenException("NoAccess", "No control rights for this room now.");

        ValidateClientKey(clientPublicKey);

        var validTo = new[] { grant.ValidTo, grant.RevokedAt ?? DateTimeOffset.MaxValue, now.AddMinutes(policy.MaxLanGrantMinutes) }.Min();
        var claims = new LanGrantClaims(grant.Id, userId, actor.Name, device.HardwareId, now.AddMinutes(-1), validTo, clientPublicKey);
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims, Json);
        var signature = signer.Sign(payload);

        return new IssuedLanGrant(
            Base64Url(payload),
            Base64Url(signature),
            signer.Algorithm,
            validTo,
            device.State?.LanEndpoint,
            device.State?.LanCertThumbprint,
            signer.PublicKey);
    }

    private static void ValidateClientKey(string clientPublicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(clientPublicKey), out _);
            if (key.KeySize != 256)
            {
                throw new ValidationException("InvalidClientKey", "Client key must be ECDSA P-256.");
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new ValidationException("InvalidClientKey", "Client key must be a base64 SubjectPublicKeyInfo.");
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
