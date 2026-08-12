namespace CartLaunchCompanion.Core.Portable;

public sealed class PublishedCartSourceLocator
{
    public string FindBest(string discoveredRoot)
    {
        var root = Path.GetFullPath(discoveredRoot);
        if (IsPublishedCart(root)) return root;

        var artifacts = Path.Combine(root, "artifacts");
        if (!Directory.Exists(artifacts)) return root;

        return Directory.EnumerateDirectories(artifacts)
            .SelectMany(version => new[]
            {
                Path.Combine(version, "windows", "CartLaunchCompanion"),
                Path.Combine(version, "linux", "CartLaunchCompanion"),
                Path.Combine(version, "staging", "CartLaunchCompanion")
            })
            .Where(IsPublishedCart)
            .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path))
            .FirstOrDefault() ?? root;
    }

    public static bool IsPublishedCart(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var system = Path.Combine(Path.GetFullPath(path), "System");
        return Directory.Exists(system) &&
               (Directory.Exists(Path.Combine(system, "Windows-x64")) ||
                Directory.Exists(Path.Combine(system, "Linux-x64")));
    }
}
