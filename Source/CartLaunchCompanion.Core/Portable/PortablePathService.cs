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
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

        var start = new DirectoryInfo(
            Path.GetFullPath(applicationBaseDirectory));

        for (var current = start;
             current is not null;
             current = current.Parent)
        {
            if (LooksLikePortableRoot(current.FullName))
            {
                var paths = PortablePaths.FromRoot(current.FullName);
                paths.EnsureWritableFolders();
                return paths;
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
                var paths = PortablePaths.FromRoot(current.FullName);
                paths.EnsureWritableFolders();
                return paths;
            }
        }

        var fallback = PortablePaths.FromRoot(start.FullName);
        fallback.EnsureWritableFolders();
        return fallback;
    }

    private static bool LooksLikePortableRoot(string path) =>
        RootMarkers.All(
            marker => Directory.Exists(Path.Combine(path, marker)));
}
