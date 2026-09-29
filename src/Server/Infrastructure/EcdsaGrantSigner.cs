using System.Security.Cryptography;
using NhatVuong.Application.Abstractions;
using NhatVuong.Contracts.Lan;

namespace NhatVuong.Server.Infrastructure;

/// <summary>
/// Signs LAN grants with an ECDSA P-256 key (ES256). Modules hold only the public key, delivered in their retained
/// config (AD-14). Rotation: replace the key file and restart; the new public key reaches every module with its next
/// config push, and grants signed by the old key stop verifying.
/// </summary>
public sealed class EcdsaGrantSigner : IGrantSigner, IDisposable
{
    private readonly ECDsa _key;

    public EcdsaGrantSigner(string keyPath, ILogger<EcdsaGrantSigner> logger)
    {
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(keyPath))
        {
            _key.ImportFromPem(File.ReadAllText(keyPath));
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);
            File.WriteAllText(keyPath, _key.ExportPkcs8PrivateKeyPem());
            logger.LogWarning("Generated a new grant-signing key at {Path}; keep it out of source control", keyPath);
        }

        PublicKey = LanSigning.ExportPublicKey(_key);
    }

    public string PublicKey { get; }

    public string Algorithm => LanSigning.Algorithm;

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose() => _key.Dispose();
}
