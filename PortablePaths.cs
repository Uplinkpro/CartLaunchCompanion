namespace CartLaunchCompanion;

internal static class PortablePaths
{
    public static string SystemDirectory { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string RootDirectory { get; } = ResolveRootDirectory();

    public static string DataDirectory => Path.Combine(SystemDirectory, "Data");

    private static string ResolveRootDirectory()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("CARTLAUNCHCOMPANION_PORTABLE_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && Directory.Exists(configuredRoot))
            return Path.GetFullPath(configuredRoot);

        var system = new DirectoryInfo(SystemDirectory);
        if (string.Equals(system.Name, "System", StringComparison.OrdinalIgnoreCase) && system.Parent is not null)
            return system.Parent.FullName;

        return SystemDirectory;
    }
}
