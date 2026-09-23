using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public interface IEmulatorInstaller
{
    Task<EmulatorInstallation> InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default);
}

/// <summary>Verified portable installs and journaled updates of recorded PPSSPP installations.</summary>
public sealed partial class PpssppInstaller : IManagedEmulatorInstaller, IDisposable
{
    private readonly string _root;
    private readonly IEmulatorRegistryStore _registry;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    public PpssppInstaller(string root) : this(root, new EmulatorRegistryStore(root),
        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
        => _ownsClient = true;
    public PpssppInstaller(string mediaRoot, string stateRoot) : this(mediaRoot, new EmulatorRegistryStore(stateRoot),
        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
        => _ownsClient = true;

    internal PpssppInstaller(string root, IEmulatorRegistryStore registry, HttpClient client)
    { _root = Path.GetFullPath(root); _registry = registry; _client = client; }

    public async Task<EmulatorInstallation> InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
    {
        Validate(release);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        var token = deadline.Token;
        var relative = $"Emulators/{release.Platform}/PPSSPP";
        var destination = EmulatorPathContract.Resolve(_root, relative);
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        using var installLock = new FileStream(EmulatorPathContract.Resolve(_root, $"Emulators/{release.Platform}/.ppsspp-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await RecoverLockedAsync(release.Platform);
        var previous = (await _registry.LoadAsync(token)).Installations.SingleOrDefault(i => i.EmulatorId == "ppsspp" && i.Platform == release.Platform);
        var adopting = ValidateTarget(previous, release.Platform, destination);
        if (previous is not null && CompareVersions(release.Version, previous.InstalledVersion!) <= 0)
            throw new IOException("This version is already installed or is older than the installed version.");
        EnsureNotRunning(destination);

        var stageRelative = $"Emulators/{release.Platform}/.ppsspp-{Guid.NewGuid():N}";
        var stage = EmulatorPathContract.Resolve(_root, stageRelative);
        Directory.CreateDirectory(stage);
        var package = Path.Combine(stage, "package");
        var payload = Path.Combine(stage, "payload");
        Directory.CreateDirectory(payload);
        try
        {
            await DownloadAsync(release, package, token);
            var executable = release.Platform == PlatformKind.Windows ? "PPSSPPWindows64.exe" : "PPSSPP.AppImage";
            if (release.PackageFormat == EmulatorPackageFormat.Zip)
            {
                await ExtractAsync(package, payload, token);
                if (File.Exists(Path.Combine(payload, "installed.txt")))
                    throw new InvalidDataException("The package is not a portable PPSSPP build.");
            }
            else
            {
                File.Move(package, Path.Combine(payload, executable));
                Directory.CreateDirectory(Path.Combine(payload, executable + ".home"));
                Directory.CreateDirectory(Path.Combine(payload, executable + ".config"));
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(Path.Combine(payload, executable),
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            if (!File.Exists(Path.Combine(payload, executable)) || new FileInfo(Path.Combine(payload, executable)).Length == 0)
                throw new InvalidDataException("The expected PPSSPP executable is missing.");
            var installation = new EmulatorInstallation
            {
                EmulatorId = release.EmulatorId, Platform = release.Platform,
                ExecutableRelativePath = relative + "/" + executable,
                InstalledVersion = release.Version, InstalledChannelId = release.ChannelId,
                InstalledAt = DateTimeOffset.UtcNow
            };
            var snapshot = previous is null && !adopting ? null : await SnapshotAsync(destination, token);
            if (previous is not null || adopting)
                await PreserveFilesAsync(destination, payload, token);
            Checkpoint?.Invoke("preserved");
            token.ThrowIfCancellationRequested();
            EnsureNotRunning(destination);
            EmulatorPathContract.Resolve(_root, relative);
            if ((await _registry.LoadAsync(token)).Installations.SingleOrDefault(i => i.EmulatorId == "ppsspp" && i.Platform == release.Platform) != previous)
                throw new IOException("The installation record changed. Refresh and try again.");
            var payloadDigest = await TreeDigestAsync(payload, token);
            if (snapshot is not null && !snapshot.SequenceEqual(await SnapshotAsync(destination, token)))
                throw new IOException("PPSSPP files changed while preparing the update. Close PPSSPP and try again.");
            token.ThrowIfCancellationRequested();
            EnsureNotRunning(destination);
            var journal = new InstallJournal(1, Path.GetFileName(stage), previous, installation, payloadDigest, adopting);
            WriteJournal(release.Platform, journal);
            Checkpoint?.Invoke("prepared");
            try
            {
                if (previous is not null || adopting)
                {
                    Directory.Move(destination, Path.Combine(stage, "backup"));
                    Checkpoint?.Invoke("backed-up");
                }
                Directory.Move(payload, destination);
                Checkpoint?.Invoke("published");
                // No cancellation during publication: recovery uses the atomic registry as the commit marker.
                await _registry.UpsertAsync(installation, CancellationToken.None);
                Checkpoint?.Invoke("committed");
                await RecoverLockedAsync(release.Platform);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                await RecoverLockedAsync(release.Platform);
                throw;
            }
            return installation;
        }
        finally
        {
            // Only our unique staging directory is eligible for cleanup.
            try
            {
                EmulatorPathContract.Resolve(_root, stageRelative);
                if (!File.Exists(JournalPath(release.Platform)) && Directory.Exists(stage)) DeleteStaging(stage);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Leave an isolated staging folder rather than mask the install outcome or follow a changed path.
            }
        }
    }

    internal static void Validate(EmulatorRelease r)
    {
        var windows = r.Platform == PlatformKind.Windows;
        if (r.EmulatorId != "ppsspp" || r.ChannelId != "stable" || r.IsPrerelease ||
            r.Architecture != Architecture.X64 || r.Platform is not (PlatformKind.Windows or PlatformKind.Linux) ||
            r.PackageFormat != (windows ? EmulatorPackageFormat.Zip : EmulatorPackageFormat.AppImage) ||
            !r.Version.StartsWith('v') || !Version.TryParse(r.Version[1..], out _) ||
            r.SizeBytes is <= 0 or > 2147483648 ||
            r.Sha256 is not { Length: 64 } || !r.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("A supported stable package with a publisher SHA-256 checksum is required.");
        var name = windows ? $"PPSSPP-{r.Version}-Windows-x64.zip" : $"PPSSPP-{r.Version}-anylinux-x86_64.AppImage";
        if (r.AssetName != name || r.DownloadUrl.AbsoluteUri != $"https://github.com/hrydgard/ppsspp/releases/download/{r.Version}/{name}")
            throw new InvalidDataException("The package URL does not match the official release.");
    }

    private async Task DownloadAsync(EmulatorRelease release, string path, CancellationToken token)
    {
        var uri = release.DownloadUrl;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/1.0");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location ?? throw new InvalidDataException("Missing download redirect.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                    uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new InvalidDataException("Unexpected download host.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.SizeBytes)
                throw new InvalidDataException("The download size does not match the release.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token)) != 0)
            {
                total += read;
                if (total > release.SizeBytes) throw new InvalidDataException("The download exceeds the declared size.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (total != release.SizeBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Download verification failed. Nothing was installed.");
            return;
        }
        throw new InvalidDataException("Too many download redirects.");
    }

    private static async Task ExtractAsync(string package, string payload, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count > 20000) throw new InvalidDataException("Too many archive entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.TrimEnd('/');
            EmulatorPathContract.ValidateRelativePath(name);
            if (IsDataPath(name))
                throw new InvalidDataException("The release package contains a reserved user-data path.");
            var type = (entry.ExternalAttributes >> 16) & 0xF000;
            if (type is not (0 or 0x8000 or 0x4000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || !names.Add(name))
                throw new InvalidDataException("Unsafe or duplicate archive entry.");
            total = checked(total + entry.Length);
            if (total > 2147483648) throw new InvalidDataException("Expanded package is too large.");
            var target = EmulatorPathContract.Resolve(payload, name);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token)) != 0)
            {
                written += read;
                if (written > entry.Length) throw new InvalidDataException("Invalid expanded size.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (written != entry.Length) throw new InvalidDataException("Incomplete archive entry.");
        }
    }

    private static void DeleteStaging(string path)
    {
        // Do not follow links if another process has modified staging.
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Staging changed unexpectedly; cleanup stopped.");
            if (item is DirectoryInfo directory) DeleteStaging(directory.FullName); else item.Delete();
        }
        Directory.Delete(path);
    }
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
