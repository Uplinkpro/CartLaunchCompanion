using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public sealed record EmulatorUninstallResult(PlatformKind Platform, bool PersonalDataPreserved, string Message);

/// <summary>Removes only recorded Companion-managed emulator folders and can retain known portable user data.</summary>
public sealed class EmulatorUninstallService
{
    private readonly string _mediaRoot;
    private readonly string _stateRoot;
    private readonly IEmulatorRegistryStore _registry;

    public EmulatorUninstallService(string mediaRoot, string stateRoot)
        : this(mediaRoot, stateRoot, new EmulatorRegistryStore(stateRoot)) { }

    internal EmulatorUninstallService(string mediaRoot, string stateRoot, IEmulatorRegistryStore registry)
    {
        _mediaRoot = Path.GetFullPath(mediaRoot);
        _stateRoot = Path.GetFullPath(stateRoot);
        _registry = registry;
    }

    public async Task<EmulatorUninstallResult> UninstallAsync(string emulatorId, PlatformKind platform,
        bool preservePersonalData = true, CancellationToken token = default, IProgress<string>? progress = null)
    {
        progress?.Report($"Checking the recorded {platform} installation…");
        token.ThrowIfCancellationRequested();
        var installation = (await _registry.LoadAsync(token)).Installations.SingleOrDefault(item =>
            item.EmulatorId == emulatorId && item.Platform == platform)
            ?? throw new IOException($"No managed {platform} installation is recorded for this emulator.");
        var destination = ValidateAndResolveInstallFolder(installation);
        if (!Directory.Exists(destination))
            throw new IOException("The recorded emulator folder is missing. Its installation record was left unchanged.");

        var parent = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(parent, $".{emulatorId}-uninstall-{Guid.NewGuid():N}");
        var backup = BackupFolder(emulatorId, platform);
        var temporaryBackup = backup + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var moved = false;
        var registryRemoved = false;
        try
        {
            if (preservePersonalData)
            {
                progress?.Report($"Preserving {platform} settings, firmware, saves, and controller data…");
                CopyPersonalData(emulatorId, platform, destination, temporaryBackup, token);
            }
            progress?.Report($"Removing the managed {platform} application folder…");
            Directory.Move(destination, staging);
            moved = true;
            progress?.Report($"Updating the emulator library for {platform}…");
            await _registry.RemoveAsync(emulatorId, platform, token);
            registryRemoved = true;
            if (preservePersonalData)
            {
                if (Directory.Exists(backup)) DeleteTree(backup);
                if (Directory.Exists(temporaryBackup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    Directory.Move(temporaryBackup, backup);
                }
            }
            else if (Directory.Exists(backup)) DeleteTree(backup);

            progress?.Report($"Cleaning up {platform} application files…");
            try { DeleteTree(staging); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new(platform, preservePersonalData,
                    $"Uninstalled, but some application files could not be cleaned up: {error.Message}");
            }
            progress?.Report($"{platform} uninstall complete.");
            return new(platform, preservePersonalData, preservePersonalData
                ? "Uninstalled. Portable settings, firmware, and saves were preserved."
                : "Uninstalled with its portable settings and saves.");
        }
        catch
        {
            if (registryRemoved) await _registry.UpsertAsync(installation, CancellationToken.None);
            if (moved && Directory.Exists(staging) && !Directory.Exists(destination)) Directory.Move(staging, destination);
            throw;
        }
        finally
        {
            if (Directory.Exists(temporaryBackup)) DeleteTree(temporaryBackup);
        }
    }

    public async Task RestorePreservedDataAsync(string emulatorId, PlatformKind platform, CancellationToken token = default)
    {
        var backup = BackupFolder(emulatorId, platform);
        if (!Directory.Exists(backup)) return;
        var installation = (await _registry.LoadAsync(token)).Installations.SingleOrDefault(item =>
            item.EmulatorId == emulatorId && item.Platform == platform);
        if (installation is null) return;
        var destination = ValidateAndResolveInstallFolder(installation);
        CopyTree(backup, destination, token, overwrite: true);
        DeleteTree(backup);
    }

    private string ValidateAndResolveInstallFolder(EmulatorInstallation installation)
    {
        EmulatorManagementJson.ValidateInstallation(installation);
        var parts = installation.ExecutableRelativePath.Split('/');
        var expectedFolder = installation.EmulatorId switch
        {
            "ppsspp" => "PPSSPP", "duckstation" => "DuckStation", "pcsx2" => "PCSX2",
            "rpcs3" => "RPCS3", "shadps4" => "shadPS4",
            _ => throw new IOException("This emulator is not managed by Emulator Companion.")
        };
        if (parts.Length < 4 || !parts[2].Equals(expectedFolder, StringComparison.Ordinal))
            throw new IOException("The recorded emulator path does not match its managed install folder.");
        return EmulatorPathContract.Resolve(_mediaRoot, string.Join('/', parts.Take(3)));
    }

    private string BackupFolder(string emulatorId, PlatformKind platform) => EmulatorPathContract.Resolve(_stateRoot,
        $"Config/EmulatorCompanion/PreservedData/{emulatorId}/{platform}");

    private static void CopyPersonalData(string id, PlatformKind platform, string source, string target, CancellationToken token)
    {
        foreach (var relative in PersonalDataPaths(id, platform))
        {
            token.ThrowIfCancellationRequested();
            var input = Path.Combine(source, relative);
            var output = Path.Combine(target, relative);
            if (Directory.Exists(input)) CopyTree(input, output, token, overwrite: true);
            else if (File.Exists(input))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(input, output, true);
            }
        }
    }

    private static IReadOnlyList<string> PersonalDataPaths(string id, PlatformKind platform)
    {
        if (platform == PlatformKind.Linux)
        {
            var appImage = id switch
            {
                "ppsspp" => "PPSSPP.AppImage", "duckstation" => "DuckStation.AppImage", "pcsx2" => "PCSX2.AppImage",
                "rpcs3" => "RPCS3.AppImage", "shadps4" => "Shadps4-sdl.AppImage", _ => ""
            };
            return appImage.Length == 0 ? [] : [appImage + ".home", appImage + ".config", "user"];
        }
        return id switch
        {
            "ppsspp" => ["memstick"],
            "duckstation" => ["settings.ini", "bios", "memcards", "savestates", "screenshots", "cheats", "covers"],
            "pcsx2" => ["bios", "inis", "inputprofiles", "memcards", "sstates", "snaps", "cheats", "cheats_ws", "textures"],
            "rpcs3" => ["config", "GuiConfigs", "InputConfigs", "dev_hdd0", "dev_hdd1", "dev_flash", "dev_flash2", "dev_flash3"],
            "shadps4" => ["user"],
            _ => []
        };
    }

    private static void CopyTree(string source, string destination, CancellationToken token, bool overwrite)
    {
        var root = new DirectoryInfo(source);
        if (root.LinkTarget is not null || (root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Personal data contains a link and cannot be preserved safely.");
        Directory.CreateDirectory(destination);
        foreach (var item in root.EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Personal data contains a link and cannot be preserved safely.");
            var output = Path.Combine(destination, item.Name);
            if (item is DirectoryInfo directory) CopyTree(directory.FullName, output, token, overwrite);
            else File.Copy(item.FullName, output, overwrite);
        }
    }

    private static void DeleteTree(string path)
    {
        foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Managed files contain a link; cleanup stopped.");
            if (item is DirectoryInfo directory) DeleteTree(directory.FullName); else item.Delete();
        }
        Directory.Delete(path);
    }
}
