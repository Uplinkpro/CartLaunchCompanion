using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CartLaunchCompanion.Core.Updating;

public sealed class GitHubRuntimeUpdateService(HttpClient httpClient) : IRuntimeUpdateService
{
    private const long MaximumDownloadBytes = 1024L * 1024 * 1024;

    public async Task<RuntimeUpdateAvailability?> CheckAsync(
        Version currentVersion,
        string platform,
        CancellationToken cancellationToken = default)
    {
        ValidatePlatform(platform);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{OfficialUpdateTrust.Repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CartLaunchCompanion", currentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var releaseVersion) || releaseVersion <= currentVersion)
            return null;

        var suffix = platform == "Windows-x64" ? "win-x64" : "linux-x64";
        var manifestName = $"update-{suffix}.json";
        var payloadName = platform == "Windows-x64" ? "runtime-win-x64.zip" : "runtime-linux-x64.tar.gz";
        Uri? manifestUri = null;
        Uri? payloadUri = null;
        long payloadBytes = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (name == manifestName)
                manifestUri = uri;
            else if (name == payloadName)
            {
                payloadUri = uri;
                payloadBytes = asset.GetProperty("size").GetInt64();
            }
        }

        if (manifestUri is null || payloadUri is null || payloadBytes is <= 0 or > MaximumDownloadBytes)
            throw new InvalidDataException("The latest release does not contain a valid update package.");

        return new RuntimeUpdateAvailability(
            releaseVersion.ToString(), manifestUri, payloadUri, payloadBytes,
            root.GetProperty("html_url").GetString() ?? "");
    }

    public async Task<PreparedRuntimeUpdate> DownloadAndPrepareAsync(
        RuntimeUpdateAvailability update,
        string cartRoot,
        string platform,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePlatform(platform);
        var stagingBase = Path.Combine(Path.GetFullPath(cartRoot), ".cartlaunch", "update-staging");
        var packageRoot = Path.Combine(stagingBase, Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(packageRoot, "runtime");
        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        var archivePath = Path.Combine(packageRoot, platform == "Windows-x64" ? "runtime.zip" : "runtime.tar.gz");
        Directory.CreateDirectory(runtimeRoot);

        try
        {
            await DownloadFileAsync(update.ManifestUri, manifestPath, RuntimeUpdateManifestJson.MaximumManifestBytes, null, cancellationToken);
            var manifest = await RuntimeUpdateManifestJson.LoadAsync(manifestPath, cancellationToken);
            using var verifier = new EcdsaUpdateSignatureVerifier(OfficialUpdateTrust.PublicKeyPem);
            if (!manifest.IsSigned || manifest.SignerKeyId != OfficialUpdateTrust.KeyId || !verifier.Verify(manifest) ||
                manifest.Platform != platform || manifest.Version != update.Version)
                throw new InvalidDataException("The downloaded update manifest is not trusted.");

            var requiredBytes = checked(manifest.Files.Sum(file => file.Length) + update.PayloadBytes);
            var drive = new DriveInfo(Path.GetPathRoot(packageRoot)!);
            if (drive.AvailableFreeSpace < requiredBytes + 128L * 1024 * 1024)
                throw new IOException("There is not enough free space on the cart for this update.");

            await DownloadFileAsync(update.PayloadUri, archivePath, MaximumDownloadBytes, progress, cancellationToken);
            if (platform == "Windows-x64")
                ExtractZip(archivePath, runtimeRoot);
            else
                ExtractTarGzip(archivePath, runtimeRoot);

            await new RuntimeIntegrityVerifier().VerifyAsync(runtimeRoot, manifest, cancellationToken);
            File.Delete(archivePath);
            progress?.Report(1);
            return new PreparedRuntimeUpdate(update.Version, platform, runtimeRoot, manifestPath);
        }
        catch
        {
            if (Directory.Exists(packageRoot))
                Directory.Delete(packageRoot, recursive: true);
            throw;
        }
    }

    private async Task DownloadFileAsync(Uri uri, string destination, long maximumBytes, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CartLaunchCompanion-Updater/1.0");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        if (length is > 0 && length > maximumBytes)
            throw new InvalidDataException("The update download exceeds its size limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
                throw new InvalidDataException("The update download exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (length is > 0)
                progress?.Report(Math.Min(0.95, (double)total / length.Value * 0.95));
        }
        await output.FlushAsync(cancellationToken);
        output.Flush(true);
    }

    private static void ExtractZip(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            var output = RuntimePathPolicy.ResolveContainedFile(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output, overwrite: false);
        }
    }

    private static void ExtractTarGzip(string archivePath, string destination)
    {
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.Directory)
                continue;
            if (entry.EntryType is not TarEntryType.RegularFile || entry.DataStream is null)
                throw new InvalidDataException("The Linux update contains an unsupported link or entry type.");
            var output = RuntimePathPolicy.ResolveContainedFile(destination, entry.Name.TrimStart('.', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            entry.DataStream.CopyTo(target);
            target.Flush(flushToDisk: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(output, entry.Mode);
        }
    }

    private static bool TryParseVersion(string value, out Version version) =>
        Version.TryParse(value.Trim().TrimStart('v', 'V').Split('-', 2)[0], out version!);

    private static void ValidatePlatform(string platform)
    {
        if (platform is not ("Windows-x64" or "Linux-x64"))
            throw new PlatformNotSupportedException("Automatic updates support Windows x64 and Linux x64 only.");
    }
}
