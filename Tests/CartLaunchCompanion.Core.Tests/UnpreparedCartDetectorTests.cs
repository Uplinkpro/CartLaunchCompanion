using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class UnpreparedCartDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-Unprepared-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void OrdinaryMediaIsIgnored()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "Photos"));

        Assert.Empty(Detector().Scan());
    }

    [Fact]
    public void CartFolderWithoutPublishedRuntimeIsIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Cart"));

        Assert.Empty(Detector().Scan());
    }

    [Fact]
    public void PublishedRuntimeWithoutIdentityIsOfferedForSetup()
    {
        CreateRuntime("Windows-x64", "CartLaunchCompanion.Desktop.exe");

        var candidate = Assert.Single(Detector().Scan());

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root)), candidate.MediaRoot);
        Assert.Contains("Windows-x64", candidate.Platforms);
        Assert.Contains("Games", candidate.MissingFolders);
    }

    [Fact]
    public async Task ExistingIdentityIsNeverOfferedForSetup()
    {
        CreateRuntime("Windows-x64", "CartLaunchCompanion.Desktop.exe");
        var identities = new CartIdentityService();
        await identities.SaveNewAsync(_root, identities.Create("Prepared"));

        Assert.Empty(Detector().Scan());
    }

    [Fact]
    public void InvalidExistingIdentityIsNeverAutomaticallyRepaired()
    {
        CreateRuntime("Linux-x64", "CartLaunchCompanion.Desktop");
        var identity = CartIdentityService.GetIdentityPath(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(identity)!);
        File.WriteAllText(identity, "not valid json");

        Assert.Empty(Detector().Scan());
    }

    [Fact]
    public void IdentityDirectoryLinkIsNeverOfferedForSetup()
    {
        if (OperatingSystem.IsWindows()) return;
        CreateRuntime("Linux-x64", "CartLaunchCompanion.Desktop");
        var outside = Path.Combine(_root, "outside-identity");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(_root, CartIdentityService.DirectoryName), outside);

        Assert.Empty(Detector().Scan());
    }

    private UnpreparedCartDetector Detector() => new(new StaticMounts(_root));

    private void CreateRuntime(string platform, string executable)
    {
        var runtime = Path.Combine(_root, "Cart", "System", platform);
        Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(runtime, executable), "published runtime");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        var identityDirectory = Path.Combine(_root, CartIdentityService.DirectoryName);
        if (Directory.Exists(identityDirectory)) new DirectoryInfo(identityDirectory).Attributes = FileAttributes.Directory;
        Directory.Delete(_root, recursive: true);
    }

    private sealed class StaticMounts(params string[] roots) : IMountRootProvider
    {
        public IEnumerable<string> GetMountedRoots() => roots;
    }
}
