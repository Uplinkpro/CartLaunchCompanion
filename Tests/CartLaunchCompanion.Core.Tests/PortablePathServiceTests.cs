using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PortablePathServiceTests : IDisposable
{
    private readonly string _temporaryRoot =
        Path.Combine(
            Path.GetTempPath(),
            "CLC-PortablePathTests-" + Guid.NewGuid());

    [Fact]
    public void Discover_FindsRootAbovePlatformBuildFolder()
    {
        var buildFolder = Path.Combine(
            _temporaryRoot,
            "System",
            "Windows-x64");

        Directory.CreateDirectory(buildFolder);
        Directory.CreateDirectory(
            Path.Combine(_temporaryRoot, "Games"));

        var service = new PortablePathService();

        var paths = service.Discover(buildFolder);

        Assert.Equal(
            Path.GetFullPath(_temporaryRoot),
            paths.Root);
        Assert.True(Directory.Exists(paths.Logs));
        Assert.True(Directory.Exists(paths.Cache));
    }

    [Fact]
    public void Discover_UsesDeveloperSolutionRoot()
    {
        var binFolder = Path.Combine(
            _temporaryRoot,
            "Source",
            "CartLaunchCompanion.Desktop",
            "bin",
            "Debug",
            "net10.0");

        Directory.CreateDirectory(binFolder);
        File.WriteAllText(
            Path.Combine(
                _temporaryRoot,
                "CartLaunchCompanion.Avalonia.sln"),
            "");

        var service = new PortablePathService();

        var paths = service.Discover(binFolder);

        Assert.Equal(
            Path.GetFullPath(_temporaryRoot),
            paths.Root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
            Directory.Delete(_temporaryRoot, recursive: true);
    }
}
