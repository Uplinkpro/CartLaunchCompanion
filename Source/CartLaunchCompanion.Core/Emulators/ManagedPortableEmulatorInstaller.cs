using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CartLaunchCompanion.Core.Platform;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace CartLaunchCompanion.Core.Emulators;

internal sealed record ManagedEmulatorProfile(
    string Id, string DisplayName, string FolderName,
    string WindowsExecutable, string LinuxExecutable,
    IReadOnlySet<string> Channels,
    Func<EmulatorRelease, bool> ReleaseValidator,
    Action<string, PlatformKind> PreparePortableData);

/// <summary>Shared verified, transactional installer for official portable emulator packages.</summary>
public abstract class ManagedPortableEmulatorInstaller : IManagedEmulatorInstaller, IDisposable
{
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private readonly string _root;
    private readonly IEmulatorRegistryStore _registry;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly ManagedEmulatorProfile _profile;
    private static readonly JsonSerializerOptions JournalOptions = new() { RespectNullableAnnotations = true };
    private sealed record InstallJournal(string StageName, EmulatorInstallation? Previous, EmulatorInstallation Next, bool Adopted);

    internal ManagedPortableEmulatorInstaller(string mediaRoot, string stateRoot, ManagedEmulatorProfile profile)
        : this(mediaRoot, new EmulatorRegistryStore(stateRoot),
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }, profile)
        => _ownsClient = true;

    internal ManagedPortableEmulatorInstaller(string root, IEmulatorRegistryStore registry, HttpClient client, ManagedEmulatorProfile profile)
    {
        _root = Path.GetFullPath(root);
        _registry = registry;
        _client = client;
        _profile = profile;
    }

    public async Task<EmulatorInstallStatus> InspectAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
    {
        await RecoverAsync(release.Platform, cancellationToken);
        var previous = (await _registry.LoadAsync(cancellationToken)).Installations
            .SingleOrDefault(item => item.EmulatorId == _profile.Id && item.Platform == release.Platform);
        try
        {
            ValidateRelease(release);
            var adopting = ValidateTarget(previous, release.Platform, Destination(release.Platform));
            if (previous is null)
                return new(EmulatorInstallAction.Install, adopting
                    ? $"An existing {_profile.DisplayName} location was found. Its data will be preserved and this release will be registered."
                    : "Ready to install.");
            if (!File.Exists(Path.Combine(Destination(release.Platform), Executable(release.Platform))))
                return new(EmulatorInstallAction.Update,
                    $"The recorded {_profile.DisplayName} program files are missing. Download this build again to repair the installation; saved data will be preserved.");
            if (previous.InstalledChannelId != release.ChannelId)
                return new(EmulatorInstallAction.Update,
                    $"Switch {previous.InstalledChannelId} to {release.ChannelId}. Settings and game data will be preserved.");
            return CompareVersions(release.Version, previous.InstalledVersion!) switch
            {
                > 0 => new(EmulatorInstallAction.Update,
                    $"Update {previous.InstalledVersion} to {release.Version}. Settings and game data will be preserved."),
                0 => new(EmulatorInstallAction.Current, $"{_profile.DisplayName} {previous.InstalledVersion} is already installed."),
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
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        var token = deadline.Token;
        var parent = EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(release.Platform)}");
        Directory.CreateDirectory(parent);
        using var installLock = new FileStream(Path.Combine(parent, $".{_profile.Id}-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await RecoverLockedAsync(release.Platform);
        var destination = Destination(release.Platform);
        var previous = (await _registry.LoadAsync(token)).Installations
            .SingleOrDefault(item => item.EmulatorId == _profile.Id && item.Platform == release.Platform);
        var adopting = ValidateTarget(previous, release.Platform, destination);
        var executableMissing = previous is not null && !File.Exists(Path.Combine(destination, Executable(release.Platform)));
        if (!executableMissing && previous?.InstalledChannelId == release.ChannelId &&
            CompareVersions(release.Version, previous.InstalledVersion!) <= 0)
            throw new IOException("This version is already installed or is older than the installed version.");
        if (executableMissing) Directory.CreateDirectory(destination);
        EnsureNotRunning(destination);

        var stageName = $".{_profile.Id}-{Guid.NewGuid():N}";
        var stage = EmulatorPathContract.Resolve(parent, stageName);
        var payload = Path.Combine(stage, "payload");
        var package = Path.Combine(stage, "package");
        Directory.CreateDirectory(payload);
        try
        {
            await DownloadAsync(release, package, token);
            var executable = Executable(release.Platform);
            switch (release.PackageFormat)
            {
                case EmulatorPackageFormat.Zip: await ExtractZipAsync(package, payload, token); break;
                case EmulatorPackageFormat.SevenZip: await ExtractSevenZipAsync(package, payload, token); break;
                case EmulatorPackageFormat.AppImage: File.Move(package, Path.Combine(payload, executable)); break;
                default: throw new InvalidDataException("Unsupported package format.");
            }
            FlattenSingleRoot(payload, executable);
            var executablePath = Path.Combine(payload, executable);
            if (!File.Exists(executablePath) || new FileInfo(executablePath).Length == 0)
                throw new InvalidDataException($"The official {_profile.DisplayName} executable is missing from the package.");
            _profile.PreparePortableData(payload, release.Platform);
            if (OperatingSystem.IsLinux() && release.Platform == PlatformKind.Linux)
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            if ((previous is not null || adopting) && Directory.Exists(destination))
                await PreserveMissingFilesAsync(destination, payload, token);

            var installation = new EmulatorInstallation
            {
                EmulatorId = _profile.Id, Platform = release.Platform,
                ExecutableRelativePath = $"Emulators/{PlatformName(release.Platform)}/{_profile.FolderName}/{executable}",
                InstalledVersion = release.Version, InstalledChannelId = release.ChannelId,
                InstalledAt = DateTimeOffset.UtcNow
            };
            await WriteJournalAsync(release.Platform, new(stageName, previous, installation, adopting), token);
            try
            {
                if ((previous is not null || adopting) && Directory.Exists(destination))
                    Directory.Move(destination, Path.Combine(stage, "backup"));
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
            try { if (!File.Exists(JournalPath(release.Platform)) && Directory.Exists(stage)) DeleteTree(stage); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task RecoverAsync(PlatformKind platform, CancellationToken cancellationToken = default)
    {
        var journal = JournalPath(platform);
        if (!File.Exists(journal)) return;
        cancellationToken.ThrowIfCancellationRequested();
        using var installLock = new FileStream(Path.Combine(Path.GetDirectoryName(journal)!, $".{_profile.Id}-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await RecoverLockedAsync(platform);
    }

    private async Task RecoverLockedAsync(PlatformKind platform)
    {
        var journalPath = JournalPath(platform);
        if (!File.Exists(journalPath)) return;
        if (new FileInfo(journalPath).Length > 65536) throw new InvalidDataException("The recovery record is too large.");
        var journal = JsonSerializer.Deserialize<InstallJournal>(await File.ReadAllTextAsync(journalPath), JournalOptions)
            ?? throw new IOException($"The interrupted {_profile.DisplayName} installation record is unreadable.");
        EmulatorPathContract.ValidateRelativePath(journal.StageName);
        if (!journal.StageName.StartsWith($".{_profile.Id}-", StringComparison.Ordinal) || journal.StageName.Contains('/'))
            throw new IOException($"The interrupted {_profile.DisplayName} installation record is invalid.");
        ValidateRecord(journal.Next, platform);
        if (journal.Previous is not null) ValidateRecord(journal.Previous, platform);
        var parent = Path.GetDirectoryName(journalPath)!;
        var stage = EmulatorPathContract.Resolve(parent, journal.StageName);
        var backup = Path.Combine(stage, "backup");
        var destination = Destination(platform);
        var current = (await _registry.LoadAsync()).Installations
            .SingleOrDefault(item => item.EmulatorId == _profile.Id && item.Platform == platform);
        if (current == journal.Next)
        {
            if (!File.Exists(Path.Combine(destination, Executable(platform))))
                throw new IOException($"The committed {_profile.DisplayName} installation is incomplete; recovery files were retained.");
        }
        else if (current == journal.Previous)
        {
            EnsureNotRunning(destination);
            if (Directory.Exists(backup))
            {
                if (Directory.Exists(destination)) DeleteTree(destination);
                Directory.Move(backup, destination);
            }
            else if (journal.Previous is null && !journal.Adopted && Directory.Exists(destination)) DeleteTree(destination);
        }
        else throw new IOException($"{_profile.DisplayName} installation records changed during recovery; recovery files were retained.");
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

    private static async Task ExtractZipAsync(string package, string payload, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count > 30000) throw new InvalidDataException("Too many archive entries.");
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.TrimEnd('/');
            if (name.Length == 0) continue;
            EmulatorPathContract.ValidateRelativePath(name);
            var type = (entry.ExternalAttributes >> 16) & 0xF000;
            if (type is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || !names.Add(name))
                throw new InvalidDataException("Unsafe or duplicate archive entry.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumPackageBytes) throw new InvalidDataException("Expanded package is too large.");
            var target = EmulatorPathContract.Resolve(payload, name);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
        }
    }

    private static async Task ExtractSevenZipAsync(string package, string payload, CancellationToken token)
    {
        using var archive = SevenZipArchive.OpenArchive(package, ReaderOptions.ForFilePath);
        var entries = archive.Entries.ToArray();
        if (entries.Length > 30000) throw new InvalidDataException("Too many archive entries.");
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.Key?.Replace('\\', '/').TrimEnd('/') ?? throw new InvalidDataException("Archive entry has no name.");
            if (name.Length == 0) continue;
            EmulatorPathContract.ValidateRelativePath(name);
            if (entry.LinkTarget is not null || !names.Add(name)) throw new InvalidDataException("Unsafe or duplicate archive entry.");
            expanded = checked(expanded + (long)entry.Size);
            if (expanded > MaximumPackageBytes) throw new InvalidDataException("Expanded package is too large.");
            var target = EmulatorPathContract.Resolve(payload, name);
            if (entry.IsDirectory) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.OpenEntryStream();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
        }
    }

    private static void FlattenSingleRoot(string payload, string executable)
    {
        if (File.Exists(Path.Combine(payload, executable))) return;
        var items = new DirectoryInfo(payload).EnumerateFileSystemInfos().ToArray();
        if (items.Length != 1 || items[0] is not DirectoryInfo root || root.LinkTarget is not null ||
            !File.Exists(Path.Combine(root.FullName, executable))) return;
        foreach (var item in root.EnumerateFileSystemInfos())
        {
            var target = Path.Combine(payload, item.Name);
            if (item is DirectoryInfo directory) Directory.Move(directory.FullName, target);
            else File.Move(item.FullName, target);
        }
        root.Delete();
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

    private void ValidateRelease(EmulatorRelease release)
    {
        if (release.EmulatorId != _profile.Id || release.Platform is not (PlatformKind.Windows or PlatformKind.Linux) ||
            release.Architecture != Architecture.X64 || !_profile.Channels.Contains(release.ChannelId) ||
            release.SizeBytes is <= 0 or > MaximumPackageBytes || release.Sha256 is not { Length: 64 } ||
            !release.Sha256.All(Uri.IsHexDigit) || !_profile.ReleaseValidator(release))
            throw new InvalidDataException("A supported official package with a publisher SHA-256 checksum is required.");
        _ = VersionParts(release.Version);
    }

    private static int CompareVersions(string candidate, string installed)
    {
        var left = VersionParts(candidate);
        var right = VersionParts(installed);
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var comparison = (index < left.Length ? left[index] : 0).CompareTo(index < right.Length ? right[index] : 0);
            if (comparison != 0) return comparison;
        }
        return StringComparer.Ordinal.Compare(candidate, installed);
    }

    private static int[] VersionParts(string value)
    {
        var matches = Regex.Matches(value, "[0-9]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        if (matches.Count == 0 || matches.Count > 8) throw new InvalidDataException("The emulator version cannot be compared safely.");
        return matches.Select(match => int.TryParse(match.Value, out var part) ? part :
            throw new InvalidDataException("The emulator version cannot be compared safely.")).ToArray();
    }

    private bool ValidateTarget(EmulatorInstallation? previous, PlatformKind platform, string destination)
    {
        if (previous is null)
        {
            if (File.Exists(destination)) throw new IOException($"An unregistered {_profile.DisplayName} file occupies the install location.");
            if (!Directory.Exists(destination)) return false;
            if (!new DirectoryInfo(destination).EnumerateFileSystemInfos().Any()) return true;
            if (File.Exists(Path.Combine(destination, Executable(platform)))) return true;
            throw new IOException($"The existing {_profile.DisplayName} folder is not recognizable and was left unchanged.");
        }
        ValidateRecord(previous, platform);
        // A valid registry record still establishes ownership of this location when program files are
        // missing. Treat the next install as a repair and preserve any remaining user data.
        return false;
    }

    private void ValidateRecord(EmulatorInstallation record, PlatformKind platform)
    {
        EmulatorManagementJson.ValidateInstallation(record);
        if (record.EmulatorId != _profile.Id || record.Platform != platform || record.InstalledVersion is null ||
            record.InstalledAt is null || record.InstalledChannelId is null || !_profile.Channels.Contains(record.InstalledChannelId) ||
            record.ExecutableRelativePath != $"Emulators/{PlatformName(platform)}/{_profile.FolderName}/{Executable(platform)}")
            throw new IOException($"This installation is not a managed {_profile.DisplayName} installation.");
        _ = VersionParts(record.InstalledVersion);
    }

    private void EnsureNotRunning(string destination)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matches = false;
                try
                {
                    if (!process.ProcessName.StartsWith(Path.GetFileNameWithoutExtension(Executable(PlatformKind.Windows)), StringComparison.OrdinalIgnoreCase) &&
                        !process.ProcessName.StartsWith(_profile.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    matches = true;
                    var path = process.MainModule?.FileName;
                    if (OperatingSystem.IsLinux() || path is null || Path.GetFullPath(path).StartsWith(
                        Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"Close {_profile.DisplayName} before installing or updating.");
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error) when (matches)
                { throw new IOException($"A running {_profile.DisplayName} process could not be checked. Close it before updating.", error); }
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
        EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(platform)}/{_profile.FolderName}");
    private string JournalPath(PlatformKind platform) =>
        EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(platform)}/.{_profile.Id}-transaction.json");
    private string Executable(PlatformKind platform) => platform == PlatformKind.Windows
        ? _profile.WindowsExecutable : _profile.LinuxExecutable;
    private static string PlatformName(PlatformKind platform) => platform switch
    {
        PlatformKind.Windows => "Windows", PlatformKind.Linux => "Linux",
        _ => throw new InvalidDataException("Unsupported installation platform.")
    };
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}

public sealed class Pcsx2Installer : ManagedPortableEmulatorInstaller
{
    private static readonly ManagedEmulatorProfile Profile = new("pcsx2", "PCSX2", "PCSX2", "pcsx2-qt.exe", "PCSX2.AppImage",
        new HashSet<string>(["stable", "nightly"], StringComparer.Ordinal), Validate,
        static (folder, platform) =>
        {
            if (platform == PlatformKind.Windows) File.WriteAllBytes(Path.Combine(folder, "portable.ini"), []);
            else { Directory.CreateDirectory(Path.Combine(folder, "PCSX2.AppImage.home")); Directory.CreateDirectory(Path.Combine(folder, "PCSX2.AppImage.config")); }
        });
    public Pcsx2Installer(string mediaRoot, string stateRoot) : base(mediaRoot, stateRoot, Profile) { }
    internal Pcsx2Installer(string root, IEmulatorRegistryStore registry, HttpClient client) : base(root, registry, client, Profile) { }
    private static bool Validate(EmulatorRelease release)
    {
        var asset = release.Platform == PlatformKind.Windows
            ? $"pcsx2-{release.Version}-windows-x64-Qt.7z" : $"pcsx2-{release.Version}-linux-appimage-x64-Qt.AppImage";
        return release.AssetName == asset && release.PackageFormat == (release.Platform == PlatformKind.Windows ? EmulatorPackageFormat.SevenZip : EmulatorPackageFormat.AppImage) &&
            release.ReleasePage.AbsoluteUri == $"https://github.com/PCSX2/pcsx2/releases/tag/{release.Version}" &&
            release.DownloadUrl.AbsoluteUri == $"https://github.com/PCSX2/pcsx2/releases/download/{release.Version}/{asset}";
    }
}

public sealed class Rpcs3Installer : ManagedPortableEmulatorInstaller
{
    private static readonly ManagedEmulatorProfile Profile = new("rpcs3", "RPCS3", "RPCS3", "rpcs3.exe", "RPCS3.AppImage",
        new HashSet<string>(["rolling"], StringComparer.Ordinal), Validate,
        static (folder, platform) =>
        {
            if (platform == PlatformKind.Linux)
            { Directory.CreateDirectory(Path.Combine(folder, "RPCS3.AppImage.home")); Directory.CreateDirectory(Path.Combine(folder, "RPCS3.AppImage.config")); }
        });
    public Rpcs3Installer(string mediaRoot, string stateRoot) : base(mediaRoot, stateRoot, Profile) { }
    internal Rpcs3Installer(string root, IEmulatorRegistryStore registry, HttpClient client) : base(root, registry, client, Profile) { }
    private static bool Validate(EmulatorRelease release)
    {
        var repo = release.Platform == PlatformKind.Windows ? "rpcs3-binaries-win" : "rpcs3-binaries-linux";
        var suffix = release.Platform == PlatformKind.Windows ? "_win64_msvc.7z" : "_linux64.AppImage";
        return release.ChannelId == "rolling" && release.IsPrerelease &&
            release.PackageFormat == (release.Platform == PlatformKind.Windows ? EmulatorPackageFormat.SevenZip : EmulatorPackageFormat.AppImage) &&
            Regex.IsMatch(release.DownloadUrl.AbsoluteUri,
                $"^https://github\\.com/RPCS3/{repo}/releases/download/build-[0-9a-f]{{40}}/rpcs3-{Regex.Escape(release.Version)}-[0-9a-f]{{8}}{Regex.Escape(suffix)}$",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) &&
            release.AssetName == Path.GetFileName(release.DownloadUrl.AbsolutePath) &&
            release.ReleasePage.Host == "github.com";
    }
}

public sealed class ShadPs4Installer : ManagedPortableEmulatorInstaller
{
    private static readonly ManagedEmulatorProfile Profile = new("shadps4", "shadPS4", "shadPS4", "shadPS4.exe", "Shadps4-sdl.AppImage",
        new HashSet<string>(["stable", "nightly"], StringComparer.Ordinal), Validate,
        static (folder, _) => Directory.CreateDirectory(Path.Combine(folder, "user")));
    public ShadPs4Installer(string mediaRoot, string stateRoot) : base(mediaRoot, stateRoot, Profile) { }
    internal ShadPs4Installer(string root, IEmulatorRegistryStore registry, HttpClient client) : base(root, registry, client, Profile) { }
    private static bool Validate(EmulatorRelease release)
    {
        var platform = release.Platform == PlatformKind.Windows ? "win64" : "linux";
        return release.PackageFormat == EmulatorPackageFormat.Zip &&
            Regex.IsMatch(release.AssetName, $"^shadps4-{platform}-sdl-[0-9A-Za-z.-]+\\.zip$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) &&
            release.DownloadUrl.AbsoluteUri == $"https://github.com/shadps4-emu/shadPS4/releases/download/{release.ReleasePage.Segments.Last()}/{release.AssetName}";
    }
}
