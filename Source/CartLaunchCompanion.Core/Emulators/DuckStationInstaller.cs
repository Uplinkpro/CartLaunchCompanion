using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Verified portable installs and recoverable updates of DuckStation Windows/Linux x64 builds.</summary>
public sealed class DuckStationInstaller : IManagedEmulatorInstaller, IDisposable
{
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private readonly string _root;
    private readonly IEmulatorRegistryStore _registry;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private static readonly JsonSerializerOptions JournalOptions = new() { RespectNullableAnnotations = true };
    private sealed record InstallJournal(string StageName, EmulatorInstallation? Previous, EmulatorInstallation Next);

    public DuckStationInstaller(string root) : this(root, new EmulatorRegistryStore(root),
        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
        => _ownsClient = true;

    public DuckStationInstaller(string mediaRoot, string stateRoot) : this(mediaRoot, new EmulatorRegistryStore(stateRoot),
        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
        => _ownsClient = true;

    internal DuckStationInstaller(string root, IEmulatorRegistryStore registry, HttpClient client)
    { _root = Path.GetFullPath(root); _registry = registry; _client = client; }

    public async Task<EmulatorInstallStatus> InspectAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
    {
        await RecoverAsync(release.Platform, cancellationToken);
        var previous = (await _registry.LoadAsync(cancellationToken)).Installations
            .SingleOrDefault(item => item.EmulatorId == "duckstation" && item.Platform == release.Platform);
        try
        {
            ValidateRelease(release);
            var adopting = ValidateTarget(previous, release.Platform, Destination(release.Platform));
            if (previous is null) return new(EmulatorInstallAction.Install, adopting
                ? "An existing DuckStation location was found. Portable settings and saves will be preserved and the current release will be registered."
                : "Ready to install.");
            if (previous.InstalledChannelId != release.ChannelId)
                return new(EmulatorInstallAction.Update,
                    $"Switch {previous.InstalledChannelId} to {release.ChannelId}. Settings, BIOS files, memory cards, and saves will be preserved.");
            var comparison = CompareVersions(release.Version, previous.InstalledVersion!, release.ChannelId);
            return comparison switch
            {
                > 0 => new(EmulatorInstallAction.Update,
                    $"Update {previous.InstalledVersion} to {release.Version}. Settings, BIOS files, memory cards, and saves will be preserved."),
                0 => new(EmulatorInstallAction.Current, $"DuckStation {previous.InstalledVersion} is already installed."),
                _ => new(EmulatorInstallAction.NewerInstalled,
                    $"Installed version {previous.InstalledVersion} is newer. Downgrades are not offered.")
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        { return new(EmulatorInstallAction.Blocked, error.Message); }
    }

    public async Task<EmulatorInstallation> InstallAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
    {
        ValidateRelease(release);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        var token = deadline.Token;
        var parent = EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(release.Platform)}");
        Directory.CreateDirectory(parent);
        using var installLock = new FileStream(Path.Combine(parent, ".duckstation-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await RecoverLockedAsync(release.Platform);

        var destination = Destination(release.Platform);
        var previous = (await _registry.LoadAsync(token)).Installations
            .SingleOrDefault(item => item.EmulatorId == "duckstation" && item.Platform == release.Platform);
        var adopting = ValidateTarget(previous, release.Platform, destination);
        if (previous?.InstalledChannelId == release.ChannelId &&
            CompareVersions(release.Version, previous.InstalledVersion!, release.ChannelId) <= 0)
            throw new IOException("This version is already installed or is older than the installed version.");
        EnsureNotRunning(destination);

        var stageName = ".duckstation-" + Guid.NewGuid().ToString("N");
        var stage = EmulatorPathContract.Resolve(parent, stageName);
        var payload = Path.Combine(stage, "payload");
        var package = Path.Combine(stage, "package");
        Directory.CreateDirectory(payload);
        try
        {
            await DownloadAsync(release, package, token);
            var executable = Executable(release.Platform);
            if (release.PackageFormat == EmulatorPackageFormat.Zip)
                await ExtractAsync(package, payload, token);
            else
                File.Move(package, Path.Combine(payload, executable));
            if (!File.Exists(Path.Combine(payload, executable)))
                throw new InvalidDataException("The official DuckStation executable is missing from the package.");
            await File.WriteAllBytesAsync(Path.Combine(payload, "portable.txt"), [], token);
            if (OperatingSystem.IsLinux() && release.Platform == PlatformKind.Linux)
                File.SetUnixFileMode(Path.Combine(payload, executable), UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            if (previous is not null || adopting) await PreserveMissingFilesAsync(destination, payload, token);

            var installation = new EmulatorInstallation
            {
                EmulatorId = "duckstation", Platform = release.Platform,
                ExecutableRelativePath = $"Emulators/{PlatformName(release.Platform)}/DuckStation/{executable}",
                InstalledVersion = release.Version, InstalledChannelId = release.ChannelId,
                InstalledAt = DateTimeOffset.UtcNow
            };
            await WriteJournalAsync(release.Platform, new(stageName, previous, installation), token);
            try
            {
                if (previous is not null || adopting) Directory.Move(destination, Path.Combine(stage, "backup"));
                Directory.Move(payload, destination);
                await _registry.UpsertAsync(installation, CancellationToken.None);
                File.Delete(JournalPath(release.Platform));
                DeleteTree(stage);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                await RecoverLockedAsync(release.Platform);
                throw;
            }
            return installation;
        }
        finally
        {
            try
            {
                if (!File.Exists(JournalPath(release.Platform)) && Directory.Exists(stage)) DeleteTree(stage);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task RecoverAsync(PlatformKind platform, CancellationToken cancellationToken = default)
    {
        var journal = JournalPath(platform);
        if (!File.Exists(journal)) return;
        cancellationToken.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(journal)!;
        using var installLock = new FileStream(Path.Combine(parent, ".duckstation-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await RecoverLockedAsync(platform);
    }

    private async Task RecoverLockedAsync(PlatformKind platform)
    {
        var journalPath = JournalPath(platform);
        if (!File.Exists(journalPath)) return;
        var journal = JsonSerializer.Deserialize<InstallJournal>(await File.ReadAllTextAsync(journalPath), JournalOptions)
            ?? throw new IOException("The interrupted DuckStation installation record is unreadable.");
        EmulatorPathContract.ValidateRelativePath(journal.StageName);
        if (!journal.StageName.StartsWith(".duckstation-", StringComparison.Ordinal) || journal.StageName.Contains('/'))
            throw new IOException("The interrupted DuckStation installation record is invalid.");
        ValidateRecord(journal.Next, platform);
        if (journal.Previous is not null) ValidateRecord(journal.Previous, platform);
        var parent = Path.GetDirectoryName(journalPath)!;
        var stage = EmulatorPathContract.Resolve(parent, journal.StageName);
        var backup = Path.Combine(stage, "backup");
        var destination = Destination(platform);
        var current = (await _registry.LoadAsync()).Installations
            .SingleOrDefault(item => item.EmulatorId == "duckstation" && item.Platform == platform);
        if (current == journal.Next)
        {
            if (!File.Exists(Path.Combine(destination, Executable(platform))))
                throw new IOException("The committed DuckStation installation is incomplete; recovery files were retained.");
        }
        else if (current == journal.Previous)
        {
            if (Directory.Exists(backup))
            {
                if (Directory.Exists(destination)) DeleteTree(destination);
                Directory.Move(backup, destination);
            }
            else if (journal.Previous is null && Directory.Exists(destination)) DeleteTree(destination);
        }
        else throw new IOException("DuckStation installation records changed during recovery; recovery files were retained.");
        File.Delete(journalPath);
        if (Directory.Exists(stage)) DeleteTree(stage);
    }

    private async Task WriteJournalAsync(PlatformKind platform, InstallJournal journal, CancellationToken token)
    {
        var path = JournalPath(platform);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(journal, JournalOptions), token);
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task DownloadAsync(EmulatorRelease release, string path, CancellationToken token)
    {
        var uri = release.DownloadUrl;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/1.0");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
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
            if (total != release.SizeBytes ||
                !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
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
            var top = name.Split('/')[0];
            if (top.Equals("portable.txt", StringComparison.OrdinalIgnoreCase) ||
                top.Equals("settings.ini", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package contains a reserved DuckStation user-data file.");
            var type = (entry.ExternalAttributes >> 16) & 0xF000;
            if (type is not (0 or 0x8000 or 0x4000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || !names.Add(name))
                throw new InvalidDataException("Unsafe or duplicate archive entry.");
            total = checked(total + entry.Length);
            if (total > MaximumPackageBytes) throw new InvalidDataException("Expanded package is too large.");
            var target = EmulatorPathContract.Resolve(payload, name);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
            if (output.Length != entry.Length) throw new InvalidDataException("Incomplete archive entry.");
        }
    }

    private static async Task PreserveMissingFilesAsync(string source, string target, CancellationToken token)
    {
        foreach (var item in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The existing portable data contains a link and cannot be migrated safely.");
            EmulatorPathContract.ValidateRelativePath(item.Name);
            var output = EmulatorPathContract.Resolve(target, item.Name);
            if (item is DirectoryInfo directory)
            {
                if (File.Exists(output)) throw new IOException("The update conflicts with an existing folder.");
                Directory.CreateDirectory(output);
                await PreserveMissingFilesAsync(directory.FullName, output, token);
            }
            else if (!File.Exists(output))
            {
                if (Directory.Exists(output)) throw new IOException("The update conflicts with an existing file.");
                await using var input = new FileStream(item.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(destination, token);
            }
        }
    }

    private static void ValidateRelease(EmulatorRelease release)
    {
        var windows = release.Platform == PlatformKind.Windows;
        var tag = release.ChannelId switch
        {
            "stable" when !release.IsPrerelease => "latest",
            "preview" when release.IsPrerelease => "preview",
            _ => throw new InvalidDataException("An official DuckStation stable or preview package is required.")
        };
        var asset = windows ? "duckstation-windows-x64-release.zip" : "DuckStation-x64.AppImage";
        if (release.EmulatorId != "duckstation" || release.Platform is not (PlatformKind.Windows or PlatformKind.Linux) ||
            release.Architecture != Architecture.X64 ||
            release.PackageFormat != (windows ? EmulatorPackageFormat.Zip : EmulatorPackageFormat.AppImage) ||
            release.AssetName != asset || release.SizeBytes is <= 0 or > MaximumPackageBytes ||
            release.Sha256 is not { Length: 64 } || !release.Sha256.All(Uri.IsHexDigit) ||
            release.ReleasePage.AbsoluteUri != $"https://github.com/stenzek/duckstation/releases/tag/{tag}" ||
            release.DownloadUrl.AbsoluteUri != $"https://github.com/stenzek/duckstation/releases/download/{tag}/{asset}")
            throw new InvalidDataException("A supported official package with a publisher SHA-256 checksum is required.");
        ParseVersion(release.Version, release.ChannelId);
    }

    private static DateTime ParseVersion(string version, string channel)
    {
        var prefix = channel + "-";
        if (!version.StartsWith(prefix, StringComparison.Ordinal) ||
            !DateTime.TryParseExact(version[prefix.Length..], "yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            throw new InvalidDataException("The DuckStation version cannot be compared safely.");
        return timestamp;
    }

    private static int CompareVersions(string candidate, string installed, string channel) =>
        ParseVersion(candidate, channel).CompareTo(ParseVersion(installed, channel));

    private static void ValidateRecord(EmulatorInstallation record, PlatformKind platform)
    {
        EmulatorManagementJson.ValidateInstallation(record);
        if (record.EmulatorId != "duckstation" || record.Platform != platform ||
            record.ExecutableRelativePath != $"Emulators/{PlatformName(platform)}/DuckStation/{Executable(platform)}" ||
            record.InstalledChannelId is not ("stable" or "preview") || record.InstalledVersion is null || record.InstalledAt is null)
            throw new IOException("This installation is not a managed DuckStation installation.");
        ParseVersion(record.InstalledVersion, record.InstalledChannelId);
    }

    private static bool ValidateTarget(EmulatorInstallation? previous, PlatformKind platform, string destination)
    {
        if (previous is null)
        {
            if (File.Exists(destination))
                throw new IOException("An unregistered DuckStation file occupies the install location. It was left unchanged.");
            if (!Directory.Exists(destination)) return false;
            if (!new DirectoryInfo(destination).EnumerateFileSystemInfos().Any()) return true;
            if (File.Exists(Path.Combine(destination, Executable(platform))) &&
                File.Exists(Path.Combine(destination, "portable.txt"))) return true;
            throw new IOException("An unregistered folder that is not a recognizable portable DuckStation installation already exists. It was left unchanged.");
        }
        ValidateRecord(previous, platform);
        if (!File.Exists(Path.Combine(destination, Executable(platform))))
            throw new IOException("The recorded DuckStation executable is missing. Restore the installation before updating.");
        return false;
    }

    private static void EnsureNotRunning(string destination)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matches = false;
                try
                {
                    if (!process.ProcessName.StartsWith("duckstation", StringComparison.OrdinalIgnoreCase)) continue;
                    matches = true;
                    var path = process.MainModule?.FileName;
                    if (OperatingSystem.IsLinux() || path is null || Path.GetFullPath(path).StartsWith(
                        Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Close DuckStation before installing or updating.");
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error) when (matches)
                { throw new IOException("A running DuckStation process could not be checked. Close it before updating.", error); }
            }
        }
    }

    private static void DeleteTree(string path)
    {
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Installation files changed unexpectedly; cleanup stopped.");
            if (item is DirectoryInfo directory) DeleteTree(directory.FullName); else item.Delete();
        }
        Directory.Delete(path);
    }

    private string Destination(PlatformKind platform) =>
        EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(platform)}/DuckStation");
    private string JournalPath(PlatformKind platform) =>
        EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(platform)}/.duckstation-transaction.json");
    private static string PlatformName(PlatformKind platform) => platform switch
    {
        PlatformKind.Windows => "Windows", PlatformKind.Linux => "Linux",
        _ => throw new InvalidDataException("Unsupported installation platform.")
    };
    private static string Executable(PlatformKind platform) => platform == PlatformKind.Windows
        ? "duckstation-qt-x64-ReleaseLTCG.exe" : "DuckStation.AppImage";

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
