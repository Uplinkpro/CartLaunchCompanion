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
