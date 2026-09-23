using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Builds PPSSPP's portable launch paths from its current cart location.</summary>
public static class PpssppPortablePaths
{
    public static string MemoryStickRoot(string mediaRoot) => Path.Combine(Path.GetFullPath(mediaRoot),
        "Emulators", "Shared", "MemorySticks", "PPSSPP");

    public static string ConfigurationPath(string executable, PlatformKind platform)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        return platform switch
        {
            PlatformKind.Windows => Path.Combine(folder, "memstick", "PSP", "SYSTEM", "ppsspp.ini"),
            PlatformKind.Linux => Path.Combine(executable + ".config", "ppsspp", "PSP", "SYSTEM", "ppsspp.ini"),
            _ => throw new NotSupportedException("PPSSPP portable paths support Windows and Linux only.")
        };
    }

    public static string ControlsPath(string executable, PlatformKind platform) =>
        Path.Combine(Path.GetDirectoryName(ConfigurationPath(executable, platform))!, "controls.ini");

    public static IReadOnlyList<string> AddLaunchArguments(string executable, PlatformKind platform,
        IReadOnlyList<string> arguments)
    {
        if (!TryMediaRoot(executable, platform, out var mediaRoot)) return arguments;
        var result = arguments.ToList();
        AddUnlessPresent(result, "--memstick=", "--memstick=" + MemoryStickRoot(mediaRoot));
        AddUnlessPresent(result, "--config=", "--config=" + ConfigurationPath(executable, platform));
        AddUnlessPresent(result, "--controlconfig=", "--controlconfig=" + ControlsPath(executable, platform));
        return result;
    }

    public static bool TryMediaRoot(string executable, PlatformKind platform, out string mediaRoot)
    {
        mediaRoot = "";
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux)) return false;
        var expectedName = platform == PlatformKind.Windows ? "PPSSPPWindows64.exe" : "PPSSPP.AppImage";
        var full = Path.GetFullPath(executable);
        if (!Path.GetFileName(full).Equals(expectedName, PathComparison())) return false;
        var emulatorFolder = Directory.GetParent(Path.GetDirectoryName(full)!);
        if (emulatorFolder?.Name is not "Windows" and not "Linux" ||
            !Path.GetFileName(Path.GetDirectoryName(full)!).Equals("PPSSPP", PathComparison()) ||
            !emulatorFolder.Name.Equals(platform.ToString(), StringComparison.Ordinal)) return false;
        var emulators = emulatorFolder.Parent;
        if (emulators is null || !emulators.Name.Equals("Emulators", PathComparison()) || emulators.Parent is null) return false;
        mediaRoot = emulators.Parent.FullName;
        return true;
    }

    private static void AddUnlessPresent(List<string> arguments, string prefix, string value)
    {
        if (!arguments.Any(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            arguments.Add(value);
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
