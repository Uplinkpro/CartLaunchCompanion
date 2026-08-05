namespace CartLaunchCompanion.Core.Library;

public sealed class GamePathResolver : IGamePathResolver
{
    public string Resolve(string gameFolder, string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolder);

        if (string.IsNullOrWhiteSpace(configuredPath))
            return "";

        var normalized = configuredPath.Replace(
            '/',
            Path.DirectorySeparatorChar);

        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(gameFolder, normalized));
    }

    public string? ResolveExisting(
        string gameFolder,
        string configuredPath)
    {
        var resolved = Resolve(gameFolder, configuredPath);

        return !string.IsNullOrWhiteSpace(resolved) &&
               File.Exists(resolved)
            ? resolved
            : null;
    }
}
