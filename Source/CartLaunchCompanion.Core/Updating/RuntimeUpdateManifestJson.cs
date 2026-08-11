using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.Updating;

public static class RuntimeUpdateManifestJson
{
    public const int MaximumManifestBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuntimeUpdateJsonContext JsonContext = new(Options);

    public static async Task<RuntimeUpdateManifest> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The update manifest was not found.", path);
        }

        if (info.Length <= 0 || info.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                $"The update manifest must be between 1 and {MaximumManifestBytes} bytes.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var manifest = await JsonSerializer.DeserializeAsync(
            stream,
            JsonContext.RuntimeUpdateManifest,
            cancellationToken);

        return manifest ?? throw new InvalidDataException("The update manifest is empty.");
    }

    public static byte[] GetUnsignedCanonicalBytes(RuntimeUpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var unsigned = new RuntimeUpdateManifest
        {
            FormatVersion = manifest.FormatVersion,
            SecurityVersion = manifest.SecurityVersion,
            Product = manifest.Product,
            Version = manifest.Version,
            Platform = manifest.Platform,
            EntryPoint = manifest.EntryPoint,
            RootFingerprint = manifest.RootFingerprint,
            SignerKeyId = manifest.SignerKeyId,
            Signature = "",
            Files = manifest.Files
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .Select(file => new RuntimeUpdateFile
                {
                    Path = file.Path,
                    Length = file.Length,
                    Sha256 = file.Sha256.ToLowerInvariant()
                })
                .ToList()
        };

        return JsonSerializer.SerializeToUtf8Bytes(unsigned, JsonContext.RuntimeUpdateManifest);
    }
}
