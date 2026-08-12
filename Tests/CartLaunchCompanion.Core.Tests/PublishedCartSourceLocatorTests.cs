using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PublishedCartSourceLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-SourceLocator-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PortableRoot_IsUsedWithoutSearchingArtifacts()
    {
        CreatePublished(_root, "Windows-x64");
        CreatePublished(Path.Combine(_root, "artifacts", "new", "windows", "CartLaunchCompanion"), "Windows-x64");

        Assert.Equal(Path.GetFullPath(_root), new PublishedCartSourceLocator().FindBest(_root));
    }

    [Fact]
    public void DevelopmentRoot_UsesNewestPublishedArtifact()
    {
        Directory.CreateDirectory(_root);
        var old = Path.Combine(_root, "artifacts", "2.2", "windows", "CartLaunchCompanion");
        var current = Path.Combine(_root, "artifacts", "2.3", "windows", "CartLaunchCompanion");
        CreatePublished(old, "Windows-x64");
        CreatePublished(current, "Windows-x64");
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));
        Directory.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(Path.GetFullPath(current), new PublishedCartSourceLocator().FindBest(_root));
    }

    [Fact]
    public void DevelopmentRootWithoutArtifacts_IsPreservedForManualSelection()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(Path.GetFullPath(_root), new PublishedCartSourceLocator().FindBest(_root));
    }

    private static void CreatePublished(string root, string platform) =>
        Directory.CreateDirectory(Path.Combine(root, "System", platform));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
