using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public enum EmulatorInstallAction { Install, Update, Current, NewerInstalled, Blocked }
public sealed record EmulatorInstallStatus(EmulatorInstallAction Action, string Message);
public interface IManagedEmulatorInstaller : IEmulatorInstaller
{
    Task<EmulatorInstallStatus> InspectAsync(EmulatorRelease release, CancellationToken cancellationToken = default);
    Task RecoverAsync(PlatformKind platform, CancellationToken cancellationToken = default);
}

public sealed partial class PpssppInstaller
{
    // Deterministic fault injection at filesystem boundaries; unused by production.
    internal Action<string>? Checkpoint { get; init; }
    internal sealed record InstallJournal(int SchemaVersion, string StageName,
        EmulatorInstallation? Previous, EmulatorInstallation Next, string PayloadDigest, bool Adopted = false);
    private static readonly JsonSerializerOptions JournalOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true
    };

    private string JournalPath(PlatformKind platform) =>
        EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(platform)}/.ppsspp-transaction.json");
    private static string PlatformName(PlatformKind platform) => platform switch
    {
        PlatformKind.Windows => "Windows", PlatformKind.Linux => "Linux",
        _ => throw new InvalidDataException("Unsupported installation platform.")
    };
    private static string Executable(PlatformKind platform) =>
        platform == PlatformKind.Windows ? "PPSSPPWindows64.exe" : "PPSSPP.AppImage";

    internal static int CompareVersions(string candidate, string installed)
    {
        static Version Parse(string value)
        {
            if (!value.StartsWith('v') || !Version.TryParse(value[1..], out var version))
                throw new InvalidDataException("The installed version cannot be compared safely.");
            return new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }
        return Parse(candidate).CompareTo(Parse(installed));
    }

    private static void ValidateRecord(EmulatorInstallation record, PlatformKind platform)
    {
        EmulatorManagementJson.ValidateInstallation(record);
        if (record.EmulatorId != "ppsspp" || record.Platform != platform ||
            record.ExecutableRelativePath != $"Emulators/{PlatformName(platform)}/PPSSPP/{Executable(platform)}" ||
            record.InstalledChannelId != "stable" || record.InstalledVersion is null || record.InstalledAt is null)
            throw new IOException("This installation is not a managed stable PPSSPP installation.");
        CompareVersions(record.InstalledVersion, record.InstalledVersion);
    }

    private static bool ValidateTarget(EmulatorInstallation? previous, PlatformKind platform, string destination)
    {
        if (previous is null)
        {
            if (File.Exists(destination))
                throw new IOException("An unregistered PPSSPP file occupies the install location. It was left unchanged.");
            if (!Directory.Exists(destination)) return false;
            if (!new DirectoryInfo(destination).EnumerateFileSystemInfos().Any()) return true;
            var executable = EmulatorPathContract.Resolve(destination, Executable(platform));
            var recognizable = File.Exists(executable) && (platform == PlatformKind.Windows
                ? Directory.Exists(Path.Combine(destination, "assets"))
                : Directory.Exists(executable + ".home") || Directory.Exists(executable + ".config"));
            if (recognizable) return true;
            throw new IOException("An unregistered folder that is not a recognizable portable PPSSPP installation already exists. It was left unchanged.");
        }
        ValidateRecord(previous, platform);
        if (!File.Exists(EmulatorPathContract.Resolve(destination, Executable(platform))))
            throw new IOException("The recorded PPSSPP executable is missing. Restore the installation before updating.");
        return false;
    }

    public async Task<EmulatorInstallStatus> InspectAsync(EmulatorRelease release, CancellationToken cancellationToken = default)
    {
        await RecoverAsync(release.Platform, cancellationToken);
        var previous = (await _registry.LoadAsync(cancellationToken)).Installations
            .SingleOrDefault(i => i.EmulatorId == "ppsspp" && i.Platform == release.Platform);
        try
        {
            Validate(release);
            var adopting = ValidateTarget(previous, release.Platform,
                EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(release.Platform)}/PPSSPP"));
            if (previous is null) return new(EmulatorInstallAction.Install, adopting
                ? "An existing PPSSPP location was found. Portable settings and saves will be preserved and the current release will be registered."
                : "Ready to install.");
            var comparison = CompareVersions(release.Version, previous.InstalledVersion!);
            return comparison switch
            {
                > 0 => new(EmulatorInstallAction.Update, $"Update {previous.InstalledVersion} to {release.Version}. Close PPSSPP first; settings and saves will be preserved."),
                0 => new(EmulatorInstallAction.Current, $"PPSSPP {previous.InstalledVersion} is already installed."),
                _ => new(EmulatorInstallAction.NewerInstalled, $"Installed version {previous.InstalledVersion} is newer. Downgrades are not offered.")
            };
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        { return new(EmulatorInstallAction.Blocked, error.Message); }
    }

    public async Task RecoverAsync(PlatformKind platform, CancellationToken cancellationToken = default)
    {
        var path = JournalPath(platform);
        if (!File.Exists(path)) return; // Ordinary library reads create no directories or lock files.
        cancellationToken.ThrowIfCancellationRequested();
        using var installLock = new FileStream(
            EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(platform)}/.ppsspp-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await RecoverLockedAsync(platform);
    }

    private void WriteJournal(PlatformKind platform, InstallJournal journal)
    {
        var path = JournalPath(platform);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, journal, JournalOptions);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task RecoverLockedAsync(PlatformKind platform)
    {
        var journalPath = JournalPath(platform);
        if (!File.Exists(journalPath)) return;
        if (new FileInfo(journalPath).Length > 65536) throw new InvalidDataException("The recovery record is too large.");
        var journal = JsonSerializer.Deserialize<InstallJournal>(await File.ReadAllTextAsync(journalPath), JournalOptions)
            ?? throw new InvalidDataException("The recovery record is empty.");
        if (journal.SchemaVersion != 1 || journal.StageName is null ||
            !journal.StageName.StartsWith(".ppsspp-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(journal.StageName[8..], "N", out _) || journal.Next is null ||
            journal.PayloadDigest is not { Length: 64 } || !journal.PayloadDigest.All(Uri.IsHexDigit) ||
            journal.Adopted && journal.Previous is not null)
            throw new InvalidDataException("Invalid PPSSPP recovery record. Files were left untouched.");
        ValidateRecord(journal.Next, platform);
        if (journal.Previous is { } previous)
        {
            ValidateRecord(previous, platform);
            if (CompareVersions(journal.Next.InstalledVersion!, previous.InstalledVersion!) <= 0)
                throw new InvalidDataException("Invalid recovery version sequence.");
        }

        var stageRelative = $"Emulators/{PlatformName(platform)}/{journal.StageName}";
        var stage = EmulatorPathContract.Resolve(_root, stageRelative);
        var backup = EmulatorPathContract.Resolve(_root, stageRelative + "/backup");
        var payload = EmulatorPathContract.Resolve(_root, stageRelative + "/payload");
        var displaced = EmulatorPathContract.Resolve(_root, stageRelative + "/displaced");
        var destination = EmulatorPathContract.Resolve(_root, $"Emulators/{PlatformName(platform)}/PPSSPP");
        var current = (await _registry.LoadAsync()).Installations.SingleOrDefault(i => i.EmulatorId == "ppsspp" && i.Platform == platform);
        if (current == journal.Next)
        {
            // Registry replacement completed. Never roll back a committed update.
            if (!File.Exists(EmulatorPathContract.Resolve(destination, Executable(platform))))
                throw new IOException("The committed PPSSPP executable is missing. Recovery files were retained.");
        }
        else if (current == journal.Previous)
        {
            EnsureNotRunning(destination);
            // After a crash, the emulator may have been launched independently. Never discard new saves.
            if ((Directory.Exists(backup) || journal.Previous is null) && Directory.Exists(destination))
                await VerifyUnchangedPayloadAsync(destination, journal.PayloadDigest);
            if (Directory.Exists(displaced))
                await VerifyUnchangedPayloadAsync(displaced, journal.PayloadDigest);
            if (journal.Previous is not null)
            {
                if (Directory.Exists(backup))
                {
                    if (Directory.Exists(destination))
                    {
                        if (Directory.Exists(payload) || Directory.Exists(displaced))
                            throw new IOException("Unexpected recovery folders. Files were retained.");
                        Directory.Move(destination, displaced);
                        Checkpoint?.Invoke("recovery-displaced");
                    }
                    Directory.Move(backup, destination);
                    Checkpoint?.Invoke("recovery-restored");
                }
                else if (!Directory.Exists(destination) ||
                         (!Directory.Exists(payload) && !Directory.Exists(displaced)))
                    throw new IOException("The previous PPSSPP folder cannot be located safely.");
                if (!File.Exists(EmulatorPathContract.Resolve(destination, Executable(platform))))
                    throw new IOException("The previous PPSSPP executable is missing.");
            }
            else if (journal.Adopted)
            {
                if (!Directory.Exists(backup))
                    throw new IOException("The original PPSSPP folder cannot be located safely.");
                if (Directory.Exists(destination))
                {
                    if (Directory.Exists(payload) || Directory.Exists(displaced))
                        throw new IOException("Unexpected recovery folders. Files were retained.");
                    Directory.Move(destination, displaced);
                    Checkpoint?.Invoke("recovery-displaced");
                }
                Directory.Move(backup, destination);
                Checkpoint?.Invoke("recovery-restored");
            }
            else if (Directory.Exists(destination))
            {
                if (Directory.Exists(payload) || Directory.Exists(displaced))
                    throw new IOException("Unexpected installation recovery folders.");
                Directory.Move(destination, displaced);
                Checkpoint?.Invoke("recovery-displaced");
            }
        }
        else throw new IOException("Installation records changed outside this update. Recovery files were retained.");

        // Remove the journal before cleanup. A crash during cleanup leaves only an inert staging folder.
        File.Delete(journalPath);
        try { if (Directory.Exists(stage)) DeleteStaging(stage); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { /* Keep inaccessible staging leftovers; the committed/restored installation is authoritative. */ }
    }

    private static bool IsDataPath(string relative)
    {
        var root = relative.Split('/')[0];
        return root.Equals("memstick", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("PPSSPP.AppImage.home", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("PPSSPP.AppImage.config", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PreserveFilesAsync(string source, string target, CancellationToken token)
    {
        // New package files take precedence, except user-data roots (rejected from packages).
        // Retain all other files missing from the new package, including custom files and installed.txt.
        foreach (var item in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            EmulatorPathContract.ValidateRelativePath(item.Name);
            var inputPath = EmulatorPathContract.Resolve(source, item.Name);
            var outputPath = EmulatorPathContract.Resolve(target, item.Name);
            if (item is DirectoryInfo)
            {
                if (File.Exists(outputPath)) throw new IOException("The package conflicts with an existing folder.");
                Directory.CreateDirectory(outputPath);
                await PreserveFilesAsync(inputPath, outputPath, token);
            }
            else if (!File.Exists(outputPath))
            {
                if (Directory.Exists(outputPath)) throw new IOException("The package conflicts with an existing file.");
                await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, token);
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(outputPath, File.GetUnixFileMode(inputPath));
            }
        }
    }

    private static async Task<string> TreeDigestAsync(string root, CancellationToken token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(await SnapshotAsync(root, token))));

    private static async Task VerifyUnchangedPayloadAsync(string root, string expected)
    {
        if (await TreeDigestAsync(root, CancellationToken.None) != expected)
            throw new IOException("PPSSPP files changed after the interruption. Both folders were retained for manual recovery.");
    }
    private static async Task<SortedDictionary<string, string>> SnapshotAsync(string root, CancellationToken token)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        async Task VisitAsync(string folder, string prefix)
        {
            foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested();
                var relative = prefix + item.Name;
                var path = EmulatorPathContract.Resolve(root, relative);
                if (item is DirectoryInfo)
                {
                    result.Add(relative, "directory");
                    await VisitAsync(path, relative + "/");
                }
                else
                {
                    await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    result.Add(relative, Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, token)));
                }
            }
        }
        await VisitAsync(root, "");
        return result;
    }
    private static void EnsureNotRunning(string destination)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var isPpsspp = false;
                try
                {
                    if (!process.ProcessName.StartsWith("PPSSPP", StringComparison.OrdinalIgnoreCase)) continue;
                    isPpsspp = true;
                    var path = process.MainModule?.FileName;
                    if (path is null || Path.GetFullPath(path).StartsWith(
                        Path.GetFullPath(destination) + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                        OperatingSystem.IsLinux()) // AppImage payloads execute from temporary mount paths.
                        throw new IOException("Close PPSSPP before installing, updating, or recovering.");
                }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
                catch (Win32Exception error)
                { if (isPpsspp) throw new IOException("A running PPSSPP process could not be checked. Close it before updating.", error); }
            }
        }
    }
}
