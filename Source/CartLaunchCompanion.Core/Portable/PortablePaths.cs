namespace CartLaunchCompanion.Core.Portable;

public sealed record PortablePaths(
    string Root,
    string System,
    string CartMonitor,
    string Maintenance,
    string Games,
    string Assets,
    string Config,
    string Schemas,
    string Logs,
    string Cache)
{
    public static PortablePaths FromRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var fullRoot = Path.GetFullPath(root);
        var systemRoot = Path.Combine(fullRoot, "System");

        return new PortablePaths(
            fullRoot,
            systemRoot,
            Path.Combine(systemRoot, "CartMonitor"),
            Path.Combine(systemRoot, "Maintenance"),
            Path.Combine(fullRoot, "Games"),
            Path.Combine(systemRoot, "Assets"),
            Path.Combine(fullRoot, "Config"),
            Path.Combine(systemRoot, "Schemas"),
            Path.Combine(fullRoot, "Logs"),
            Path.Combine(systemRoot, "Cache"));
    }

    public void EnsureWritableFolders()
    {
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Cache);
    }
}
