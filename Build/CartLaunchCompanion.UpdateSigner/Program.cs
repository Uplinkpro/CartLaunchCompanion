using System.Security.Cryptography;
using CartLaunchCompanion.Core.Updating;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: UpdateSigner <payload-root> <platform> <version> <manifest-output>");
    return 2;
}

var payloadRoot = Path.GetFullPath(args[0]);
var platform = args[1];
var version = args[2];
var output = Path.GetFullPath(args[3]);
var privateKeyPem = Environment.GetEnvironmentVariable("CLC_UPDATE_SIGNING_KEY_PEM");
if (string.IsNullOrWhiteSpace(privateKeyPem))
{
    Console.Error.WriteLine("CLC_UPDATE_SIGNING_KEY_PEM is not configured.");
    return 3;
}

if (platform is not ("Windows-x64" or "Linux-x64"))
    throw new InvalidDataException("Unsupported update platform.");

var files = new List<RuntimeUpdateFile>();
foreach (var path in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
             .OrderBy(path => Path.GetRelativePath(payloadRoot, path), StringComparer.Ordinal))
{
    var info = new FileInfo(path);
    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Update payload links are not allowed.");

    await using var stream = File.OpenRead(path);
    files.Add(new RuntimeUpdateFile
    {
        Path = Path.GetRelativePath(payloadRoot, path).Replace('\\', '/'),
        Length = info.Length,
        Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream))
    });
}

var manifest = new RuntimeUpdateManifest
{
    Version = version,
    Platform = platform,
    EntryPoint = platform == "Windows-x64"
        ? "CartLaunchCompanion.Desktop.exe"
        : "CartLaunchCompanion.Desktop",
    SignerKeyId = OfficialUpdateTrust.KeyId,
    Files = files,
    RootFingerprint = RuntimeIntegrityVerifier.ComputeRootFingerprint(files)
};

using var signingKey = ECDsa.Create();
signingKey.ImportFromPem(privateKeyPem);
manifest.Signature = Convert.ToBase64String(
    signingKey.SignData(RuntimeUpdateManifestJson.GetUnsignedCanonicalBytes(manifest), HashAlgorithmName.SHA256));

// Refuse to produce an official manifest when the configured secret does not
// correspond to the public key compiled into every CLC updater.
using var officialVerifier = new EcdsaUpdateSignatureVerifier(OfficialUpdateTrust.PublicKeyPem);
if (!officialVerifier.Verify(manifest))
{
    Console.Error.WriteLine(
        $"CLC_UPDATE_SIGNING_KEY_PEM does not match {OfficialUpdateTrust.KeyId}. No manifest was written.");
    return 4;
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await RuntimeUpdateManifestJson.SaveAsync(output, manifest);
Console.WriteLine($"Signed {files.Count} files for {platform} {version}.");
return 0;
