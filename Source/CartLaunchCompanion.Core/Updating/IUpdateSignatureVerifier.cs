using System.Security.Cryptography;

namespace CartLaunchCompanion.Core.Updating;

public interface IUpdateSignatureVerifier
{
    bool Verify(RuntimeUpdateManifest manifest);
}

public sealed class RejectUnsignedUpdateVerifier : IUpdateSignatureVerifier
{
    public bool Verify(RuntimeUpdateManifest manifest) => false;
}

public sealed class EcdsaUpdateSignatureVerifier : IUpdateSignatureVerifier, IDisposable
{
    private readonly ECDsa _publicKey = ECDsa.Create();

    public EcdsaUpdateSignatureVerifier(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        _publicKey.ImportFromPem(publicKeyPem);
    }

    public bool Verify(RuntimeUpdateManifest manifest)
    {
        try
        {
            var signature = Convert.FromBase64String(manifest.Signature);
            var payload = RuntimeUpdateManifestJson.GetUnsignedCanonicalBytes(manifest);
            return _publicKey.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public void Dispose() => _publicKey.Dispose();
}

public sealed class TrustedUpdateSignatureVerifier : IUpdateSignatureVerifier, IDisposable
{
    private readonly IReadOnlyDictionary<string, ECDsa> _trustedKeys;

    public TrustedUpdateSignatureVerifier(
        IEnumerable<KeyValuePair<string, string>> trustedPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(trustedPublicKeys);
        var keys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        try
        {
            foreach (var (keyId, publicKeyPem) in trustedPublicKeys)
            {
                if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(publicKeyPem) ||
                    keys.ContainsKey(keyId))
                {
                    throw new ArgumentException("Trusted update key IDs and public keys must be unique and non-empty.");
                }

                var key = ECDsa.Create();
                key.ImportFromPem(publicKeyPem);
                keys.Add(keyId, key);
            }

            if (keys.Count == 0)
            {
                throw new ArgumentException("At least one trusted update key is required.");
            }

            _trustedKeys = keys;
        }
        catch
        {
            foreach (var key in keys.Values)
            {
                key.Dispose();
            }

            throw;
        }
    }

    public static TrustedUpdateSignatureVerifier CreateOfficial() =>
        new(OfficialUpdateTrust.TrustedKeyIds.Select(keyId =>
        {
            OfficialUpdateTrust.TryGetPublicKey(keyId, out var publicKeyPem);
            return KeyValuePair.Create(keyId, publicKeyPem);
        }));

    public bool Verify(RuntimeUpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!_trustedKeys.TryGetValue(manifest.SignerKeyId, out var publicKey))
        {
            return false;
        }

        try
        {
            var signature = Convert.FromBase64String(manifest.Signature);
            var payload = RuntimeUpdateManifestJson.GetUnsignedCanonicalBytes(manifest);
            return publicKey.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var key in _trustedKeys.Values)
        {
            key.Dispose();
        }
    }
}
