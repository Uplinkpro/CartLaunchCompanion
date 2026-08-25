using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Configurator;

public sealed record InstalledEmulatorOption(
    EmulatorDefinition Definition,
    string? WindowsExecutable,
    string? LinuxExecutable)
{
    public bool HasWindows => WindowsExecutable is not null;
    public bool HasLinux => LinuxExecutable is not null;
    public string Availability => HasWindows && HasLinux ? "Windows + Linux" : HasWindows ? "Windows" : "Linux";
    public override string ToString() => $"{Definition.DisplayName} — {Availability}";
}

public sealed class EmulatorLibraryService
{
    public IReadOnlyList<InstalledEmulatorOption> Scan(string mediaRoot, bool includeMissing = false) =>
        EmulatorLaunchPresetCatalog.All
            .Select(definition => new InstalledEmulatorOption(
                definition,
                FindExecutable(Path.Combine(mediaRoot, "Emulators", "Windows", definition.FolderName), definition, true),
                FindExecutable(Path.Combine(mediaRoot, "Emulators", "Linux", definition.FolderName), definition, false)))
            .Where(item => includeMissing || item.HasWindows || item.HasLinux)
            .ToArray();

    public string ExpectedFolder(string mediaRoot, EmulatorDefinition definition, bool windows) =>
        Path.Combine(mediaRoot, "Emulators", windows ? "Windows" : "Linux", definition.FolderName);

    public bool IsInExpectedFolder(string mediaRoot, EmulatorDefinition definition, bool windows, string executable)
    {
        var expected = Path.GetFullPath(ExpectedFolder(mediaRoot, definition, windows));
        if (!Path.EndsInDirectorySeparator(expected)) expected += Path.DirectorySeparatorChar;
        return Path.GetFullPath(executable).StartsWith(expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            EmulatorLaunchPresetCatalog.Detect(executable) == definition.Id;
    }

    private static string? FindExecutable(string folder, EmulatorDefinition definition, bool windows)
    {
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => EmulatorLaunchPresetCatalog.Detect(path) == definition.Id)
            .OrderByDescending(path => windows
                ? Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                : Path.GetExtension(path).Equals(".AppImage", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }
}
