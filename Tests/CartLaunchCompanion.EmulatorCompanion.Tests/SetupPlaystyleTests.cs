using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using Xunit;

namespace CartLaunchCompanion.EmulatorCompanion.Tests;

public sealed class SetupPlaystyleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-playstyle-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetupRestoresLibraryPlaystyle()
    {
        await new EmulatorSetupPreferencesStore(_root).SaveSelectedPresetAsync("four-k");
        var model = new PortableSetupViewModel(new PortableEmulatorSetupService(_root, _root, "duckstation"));

        await model.RefreshAsync();

        Assert.Equal("four-k", model.SelectedPreset?.Id);
    }

    [Fact]
    public async Task FirstOpenRecoversPlaystyleFromAppliedDuckStationSettings()
    {
        var executable = Path.Combine(_root, "Emulators", "Windows", "DuckStation", "duckstation-qt-x64-ReleaseLTCG.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "fixture");
        await new EmulatorRegistryStore(_root).UpsertAsync(new()
        {
            EmulatorId = "duckstation", Platform = PlatformKind.Windows,
            ExecutableRelativePath = "Emulators/Windows/DuckStation/duckstation-qt-x64-ReleaseLTCG.exe",
            InstalledVersion = "test", InstalledChannelId = "stable", InstalledAt = DateTimeOffset.UtcNow
        });
        var setup = new PortableEmulatorSetupService(_root, _root, "duckstation");
        var target = Assert.Single(await setup.InspectAsync());
        await setup.ApplyAsync(target, SimpleEmulationPresetCatalog.Get("quality"));

        var model = new PortableSetupViewModel(setup);
        await model.RefreshAsync();

        Assert.Equal("quality", model.SelectedPreset?.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
