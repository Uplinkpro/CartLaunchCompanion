namespace CartLaunchCompanion.Core.Portable;

public sealed record PortablePaths(
    string Root,
    string System,
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

        return new PortablePaths(
            fullRoot,
            Path.Combine(fullRoot, "System"),
            Path.Combine(fullRoot, "Games"),
            Path.Combine(fullRoot, "Assets"),
            Path.Combine(fullRoot, "Config"),
            Path.Combine(fullRoot, "Schemas"),
            Path.Combine(fullRoot, "Logs"),
            Path.Combine(fullRoot, "Cache"));
    }

    public void EnsureWritableFolders()
    {
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Cache);
    }
}
