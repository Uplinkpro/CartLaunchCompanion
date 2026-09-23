using CartLaunchCompanion.Core.Emulators;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorStorageLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-emulator-layout-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FlatLayoutKeepsStateAndEmulatorsTogether()
    {
        var app = Path.Combine(_root, "CartLaunchCompanion");
        Directory.CreateDirectory(app);
        var layout = EmulatorStorageLayout.FromApplicationRoot(app);
        Assert.Equal(app, layout.StateRoot);
        Assert.Equal(app, layout.MediaRoot);
    }

    [Fact]
    public void NestedCartLayoutUsesSiblingEmulatorRoot()
    {
        var app = Path.Combine(_root, "Cart");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.Combine(_root, "Emulators"));
        var layout = EmulatorStorageLayout.FromApplicationRoot(app);
        Assert.Equal(app, layout.StateRoot);
        Assert.Equal(_root, layout.MediaRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
