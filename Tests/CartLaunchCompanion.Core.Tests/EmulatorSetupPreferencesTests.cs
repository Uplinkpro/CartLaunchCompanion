using CartLaunchCompanion.Core.Emulators;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorSetupPreferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-setup-preferences-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SelectedPlaystyleRoundTrips()
    {
        var store = new EmulatorSetupPreferencesStore(_root);

        Assert.Null((await store.LoadAsync()).SelectedPresetId);
        await store.SaveSelectedPresetAsync("quality");

        Assert.Equal("quality", (await store.LoadAsync()).SelectedPresetId);
    }

    [Fact]
    public async Task UnknownPlaystyleIsRejectedWithoutReplacingSavedChoice()
    {
        var store = new EmulatorSetupPreferencesStore(_root);
        await store.SaveSelectedPresetAsync("four-k");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveSelectedPresetAsync("unknown"));

        Assert.Equal("four-k", (await store.LoadAsync()).SelectedPresetId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
