namespace CartLaunchCompanion.Core.Portable;

public sealed class PortablePathService : IPortablePathService
{
    private static readonly string[] RootMarkers =
    [
        "Games",
        "System"
    ];

    public PortablePaths Discover(string applicationBaseDirectory)
    {
        var paths = DiscoverReadOnly(applicationBaseDirectory);
        paths.EnsureWritableFolders();
        return paths;
    }

    /// <summary>Uses the same root discovery without creating folders on the media.</summary>
    public PortablePaths DiscoverReadOnly(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

        var start = new DirectoryInfo(
            Path.GetFullPath(applicationBaseDirectory));

        for (var current = start;
             current is not null;
             current = current.Parent)
        {
            if (LooksLikePortableRoot(current.FullName))
            {
                return PortablePaths.FromRoot(current.FullName);
            }
        }

        // Developer fallback: when running directly from bin/Debug or
        // bin/Release, walk upward until a solution or Source folder is found.
        for (var current = start;
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "CartLaunchCompanion.Avalonia.sln")) ||
                Directory.Exists(
                    Path.Combine(current.FullName, "Source")))
            {
                return PortablePaths.FromRoot(current.FullName);
            }
        }

        return PortablePaths.FromRoot(start.FullName);
    }

    private static bool LooksLikePortableRoot(string path) =>
        RootMarkers.All(
            marker => Directory.Exists(Path.Combine(path, marker)));
}
