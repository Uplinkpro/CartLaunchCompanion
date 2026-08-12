namespace CartLaunchCompanion.Core.Portable;

public sealed class PublishedCartSourceLocator
{
    public string FindBest(string discoveredRoot)
    {
        var root = Path.GetFullPath(discoveredRoot);
        if (IsPublishedCart(root)) return root;

        var artifacts = Path.Combine(root, "artifacts");
        if (!Directory.Exists(artifacts)) return root;

        var candidates = Directory.EnumerateDirectories(artifacts)
            .SelectMany(version => new[]
            {
                new Candidate(Path.Combine(version, "windows", "CartLaunchCompanion"), "Windows-x64"),
                new Candidate(Path.Combine(version, "linux", "CartLaunchCompanion"), "Linux-x64"),
                new Candidate(Path.Combine(version, "staging", "CartLaunchCompanion"), "Any")
            })
            .Where(item => IsPublishedCart(item.Path))
            .ToList();
        var preferred = OperatingSystem.IsWindows() ? "Windows-x64" : "Linux-x64";
        return candidates
            .OrderByDescending(item => item.Platform == preferred)
            .ThenByDescending(item => Directory.GetLastWriteTimeUtc(item.Path))
            .Select(item => item.Path)
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

    private sealed record Candidate(string Path, string Platform);
}
