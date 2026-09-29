using System.Security.Cryptography;
using System.Text;
using NhatVuong.Contracts.Mqtt;

namespace NhatVuong.Contracts.Lan;

/// <summary>Server-signed grant as issued by the API: base64url JSON claims plus signature (AD-3).</summary>
public sealed record LanGrantDto(string Payload, string Signature, string Algorithm);

public sealed record LanGrantClaimsDto(
    Guid GrantId,
    Guid SubjectUserId,
    string SubjectName,
    string DeviceId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string ClientPublicKey);

/// <summary>
/// A command sent straight to a module over the campus LAN (US-24). <see cref="Signature"/> is made with the client
/// key named in the grant, over <see cref="LanSigning.CanonicalCommand"/> — proof of possession, so an observed
/// grant or command cannot be replayed by another party.
/// </summary>
public sealed record LanCommandRequest(
    LanGrantDto Grant,
    Guid CommandId,
    CommandAction Action,
    string? Value,
    DateTimeOffset IssuedAt,
    string Signature);

public sealed record LanCommandResponse(bool Success, string? Error, BoardStateDto? State);

public static class LanErrors
{
    public const string BadGrant = "bad-grant";
    public const string GrantExpired = "grant-expired";
    public const string GrantRevoked = "grant-revoked";
    public const string WrongDevice = "wrong-device";
    public const string BadSignature = "bad-signature";
    public const string Stale = "stale-command";
    public const string ClockUntrusted = "clock-untrusted";
    public const string Rejected = "rejected";
}

/// <summary>ECDSA P-256 / SHA-256, IEEE P1363 signatures — available on every .NET target (Android, Windows) and on ESP32 mbedTLS.</summary>
public static class LanSigning
{
    public const string Algorithm = "ES256";

    /// <summary>Maximum clock difference between app and module for a LAN command to be accepted.</summary>
    public static readonly TimeSpan MaxSkew = TimeSpan.FromSeconds(90);

    public static string CanonicalCommand(string deviceId, Guid commandId, CommandAction action, string? value, DateTimeOffset issuedAt) =>
        $"nvc-lan-v1|{deviceId}|{commandId:D}|{action}|{value}|{issuedAt.UtcDateTime:O}";

    public static string SignCommand(ECDsa clientKey, string deviceId, Guid commandId, CommandAction action, string? value, DateTimeOffset issuedAt)
    {
        var data = Encoding.UTF8.GetBytes(CanonicalCommand(deviceId, commandId, action, value, issuedAt));
        return Base64Url.Encode(clientKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public static bool TryVerifyGrant(LanGrantDto grant, string serverPublicKey, out LanGrantClaimsDto? claims)
    {
        claims = null;
        try
        {
            var payload = Base64Url.Decode(grant.Payload);
            if (!Verify(serverPublicKey, payload, Base64Url.Decode(grant.Signature)))
            {
                return false;
            }

            claims = NvcJson.Deserialize<LanGrantClaimsDto>(payload);
            return claims is not null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public static bool VerifyCommand(LanCommandRequest request, LanGrantClaimsDto claims)
    {
        try
        {
            var data = Encoding.UTF8.GetBytes(CanonicalCommand(claims.DeviceId, request.CommandId, request.Action, request.Value, request.IssuedAt));
            return Verify(claims.ClientPublicKey, data, Base64Url.Decode(request.Signature));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static string ExportPublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static bool Verify(string publicKeyBase64, byte[] data, byte[] signature)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}

public static class Base64Url
{
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Convert.FromBase64String(s);
    }
}
