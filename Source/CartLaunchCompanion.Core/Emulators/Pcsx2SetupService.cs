using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Emulators;

public sealed record Pcsx2SetupStatus(
    PlatformKind Platform,
    string ExecutablePath,
    string BiosFolder,
    bool BiosFound,
    bool SettingsCreated)
{
    public string State => !BiosFound ? "BIOS needed" : !SettingsCreated ? "Finish setup in PCSX2" : "Ready to test";
    public string Guidance => !BiosFound
        ? "Import a BIOS dumped from your own PlayStation 2 console."
        : !SettingsCreated
            ? "Your BIOS is in place. Open PCSX2 to choose the controller, renderer, and internal resolution."
            : "Portable settings and a BIOS were found. Open PCSX2 for a final controller check, then launch a game.";
}

/// <summary>Inspects and prepares user-owned PCSX2 setup data without interpreting PCSX2 configuration files.</summary>
public sealed class Pcsx2SetupService(string mediaRoot, string stateRoot)
{
    private readonly string _mediaRoot = Path.GetFullPath(mediaRoot);
    private readonly IEmulatorRegistryStore _registry = new EmulatorRegistryStore(stateRoot);
    private static readonly HashSet<string> BiosExtensions =
        new([".bin", ".rom", ".nvm", ".mec", ".erom"], StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<Pcsx2SetupStatus>> InspectAsync(CancellationToken cancellationToken = default)
    {
        var installations = (await _registry.LoadAsync(cancellationToken)).Installations
            .Where(item => item.EmulatorId == "pcsx2")
            .OrderBy(item => item.Platform)
            .ToArray();
        return installations.Select(Inspect).ToArray();
    }

    public async Task<Pcsx2SetupStatus> ImportBiosAsync(
        PlatformKind platform, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default)
        => (await ImportBiosAsync([platform], sourcePaths, cancellationToken)).Single();

    public async Task<IReadOnlyList<Pcsx2SetupStatus>> ImportBiosAsync(
        IEnumerable<PlatformKind> platforms, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default)
    {
        var requestedPlatforms = platforms.Distinct().ToArray();
        if (requestedPlatforms.Length == 0) throw new InvalidDataException("Choose at least one installed platform.");
        var installed = await InspectAsync(cancellationToken);
        var targets = requestedPlatforms.Select(platform => installed.SingleOrDefault(item => item.Platform == platform)
            ?? throw new IOException($"PCSX2 is not installed for {platform}.")).ToArray();
        var sources = sourcePaths.Distinct(PathComparer()).ToArray();
        if (sources.Length == 0) throw new InvalidDataException("Choose at least one BIOS file.");
        var files = sources.Select(source => new FileInfo(Path.GetFullPath(source))).ToArray();
        foreach (var info in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A selected BIOS file is unavailable or is a link.");
            if (!BiosExtensions.Contains(info.Extension) || info.Length is <= 0 or > 16 * 1024 * 1024)
                throw new InvalidDataException($"{info.Name} is not a supported PS2 BIOS file.");
        }
        var importedPrimary = files.Any(info => info.Extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
            info.Extension.Equals(".rom", StringComparison.OrdinalIgnoreCase));
        if (!importedPrimary && targets.Any(status => !status.BiosFound))
            throw new InvalidDataException("Choose the main .bin or .rom BIOS dump along with any companion files.");

        // Preflight every destination before copying so a conflict on one platform cannot partly update the other.
        foreach (var status in targets)
        {
            foreach (var info in files)
            {
                EmulatorPathContract.ValidateRelativePath(info.Name);
                var destination = EmulatorPathContract.Resolve(status.BiosFolder, info.Name);
                if (File.Exists(destination) && !await FilesMatchAsync(info.FullName, destination, cancellationToken))
                    throw new IOException($"A different file named {info.Name} already exists in the PCSX2 BIOS folder.");
            }
        }

        var created = new List<string>();
        try
        {
            foreach (var status in targets)
            {
                Directory.CreateDirectory(status.BiosFolder);
                foreach (var info in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = EmulatorPathContract.Resolve(status.BiosFolder, info.Name);
                    if (File.Exists(destination)) continue;
                    var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await using var input = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            await input.CopyToAsync(output, cancellationToken);
                            await output.FlushAsync(cancellationToken);
                            output.Flush(true);
                        }
                        File.Move(temporary, destination);
                        created.Add(destination);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
        }
        catch
        {
            foreach (var path in created) try { File.Delete(path); } catch (IOException) { }
            throw;
        }
        return targets.Select(status => Inspect(status.Platform, status.ExecutablePath)).ToArray();
    }

    private Pcsx2SetupStatus Inspect(EmulatorInstallation installation)
    {
        if (installation.Platform is not (PlatformKind.Windows or PlatformKind.Linux))
            throw new InvalidDataException("Unsupported PCSX2 platform.");
        var executable = EmulatorPathContract.Resolve(_mediaRoot, installation.ExecutableRelativePath);
        return Inspect(installation.Platform, executable);
    }

    private Pcsx2SetupStatus Inspect(PlatformKind platform, string executable)
    {
        if (!File.Exists(executable)) throw new IOException("The recorded PCSX2 executable is missing.");
        var folder = Path.GetDirectoryName(executable)!;
        SharedEmulatorResourceLayout.Prepare(_mediaRoot, "PCSX2");
        var bios = SharedEmulatorResourceLayout.GetPath(_mediaRoot, "BIOS", "PCSX2");
        CopyLegacyBios(EmulatorPathContract.Resolve(folder, "bios"), bios);
        var biosFound = Directory.Exists(bios) && Directory.EnumerateFiles(bios)
            .Any(path => new FileInfo(path) is { Length: > 0 } info &&
                (info.Extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
                 info.Extension.Equals(".rom", StringComparison.OrdinalIgnoreCase)));
        var settingsRoots = platform == PlatformKind.Windows
            ? new[] { Path.Combine(folder, "inis") }
            : new[] { executable + ".config", executable + ".home" };
        var settingsCreated = settingsRoots.Any(ContainsIniSafely);
        return new(platform, executable, bios, biosFound, settingsCreated);
    }

    private static void CopyLegacyBios(string legacy, string shared)
    {
        if (!Directory.Exists(legacy)) return;
        Directory.CreateDirectory(shared);
        foreach (var file in new DirectoryInfo(legacy).EnumerateFiles())
        {
            if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !BiosExtensions.Contains(file.Extension) || file.Length is <= 0 or > 16 * 1024 * 1024) continue;
            var destination = Path.Combine(shared, file.Name);
            if (!File.Exists(destination)) File.Copy(file.FullName, destination);
        }
    }

    private static bool ContainsIniSafely(string root)
    {
        if (!Directory.Exists(root)) return false;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new(root));
        var visited = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if (++visited > 5000) return false;
                if (item.LinkTarget is not null || (item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (item is DirectoryInfo child) pending.Push(child);
                else if (item is FileInfo file && file.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) && file.Length > 0) return true;
            }
        }
        return false;
    }

    private static async Task<bool> FilesMatchAsync(string left, string right, CancellationToken token)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length) return false;
        await using var leftStream = File.OpenRead(left);
        await using var rightStream = File.OpenRead(right);
        var leftHash = await System.Security.Cryptography.SHA256.HashDataAsync(leftStream, token);
        var rightHash = await System.Security.Cryptography.SHA256.HashDataAsync(rightStream, token);
        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
