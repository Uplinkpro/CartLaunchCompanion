using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using Xunit;

namespace CartLaunchCompanion.EmulatorCompanion.Tests;

public sealed class UpdateSettingsViewModelTests
{
    private sealed class Store : IEmulatorUpdateSettings
    {
        public EmulatorUpdatePreferences Preferences = new();
        public string? Windows = "v1.20.4", Linux = "v1.20.3";
        public Exception? LoadError, SaveError, ClearError;
        public bool WaitForCancellation;
        public int Saves;
        public async Task<EmulatorUpdatePreferences> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (LoadError is not null) throw LoadError;
            return Preferences;
        }
        public Task SaveAsync(EmulatorUpdatePreferences preferences, CancellationToken cancellationToken = default)
        {
            if (SaveError is not null) throw SaveError;
            Saves++;
            Preferences = preferences;
            return Task.CompletedTask;
        }
        public Task<string?> GetSkippedVersionAsync(PlatformKind platform, CancellationToken cancellationToken = default) =>
            Task.FromResult(platform == PlatformKind.Windows ? Windows : Linux);
        public Task ClearSkippedVersionAsync(PlatformKind platform, CancellationToken cancellationToken = default)
        {
            if (ClearError is not null) throw ClearError;
            if (platform == PlatformKind.Windows) Windows = null; else Linux = null;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PreferencesAreOnlyPersistedByExplicitSave()
    {
        var store = new Store();
        using var model = new UpdateSettingsViewModel(store);
        await model.LoadAsync();
        Assert.False(model.CanSave);
        Assert.True(model.CheckBeforeLaunch);
        model.CheckBeforeLaunch = false;
        model.SelectedInterval = model.Intervals.Single(i => i.Hours == 168);
        Assert.True(model.CanSave);
        Assert.Equal(0, store.Saves);
        await model.SaveAsync();
        Assert.Equal(1, store.Saves);
        Assert.False(store.Preferences.CheckBeforeLaunch);
        Assert.Equal(168, store.Preferences.CheckIntervalHours);
        Assert.False(model.CanSave);
    }

    [Fact]
    public async Task FailedSaveKeepsUnsavedChangesForRetry()
    {
        var store = new Store { SaveError = new IOException("Read-only library") };
        using var model = new UpdateSettingsViewModel(store);
        await model.LoadAsync();
        model.CheckBeforeLaunch = false;
        await model.SaveAsync();
        Assert.True(model.CanSave);
        Assert.True(store.Preferences.CheckBeforeLaunch);
        Assert.Contains("not saved", model.Message);
        store.SaveError = null;
        await model.SaveAsync();
        Assert.False(model.CanSave);
    }

    [Fact]
    public async Task ClearSkipDoesNotSaveUnrelatedPreferenceEdits()
    {
        var store = new Store();
        using var model = new UpdateSettingsViewModel(store);
        await model.LoadAsync();
        model.CheckBeforeLaunch = false;
        await model.ClearSkippedAsync(PlatformKind.Windows);
        Assert.Null(model.WindowsSkippedVersion);
        Assert.False(model.CanClearWindows);
        Assert.Equal("v1.20.3", model.LinuxSkippedVersion);
        Assert.True(model.CanClearLinux);
        Assert.True(model.CanSave);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task FailedClearRetainsVersionAndShowsError()
    {
        var store = new Store { ClearError = new IOException("Cache is busy") };
        using var model = new UpdateSettingsViewModel(store);
        await model.LoadAsync();
        await model.ClearSkippedAsync(PlatformKind.Windows);
        Assert.Equal("v1.20.4", model.WindowsSkippedVersion);
        Assert.True(model.CanClearWindows);
        Assert.Contains("not cleared", model.Message);
    }

    [Fact]
    public async Task CorruptPreferencesDisableSaveWithoutBlockingSkipManagement()
    {
        var store = new Store { LoadError = new InvalidDataException("Corrupt settings") };
        using var model = new UpdateSettingsViewModel(store);
        await model.LoadAsync();
        Assert.False(model.CanEdit);
        Assert.False(model.CanSave);
        Assert.True(model.CanClearWindows);
        Assert.Contains("could not be loaded", model.Message);
    }

    [Fact]
    public async Task ClosingDuringLoadCancelsWithoutWriting()
    {
        var store = new Store { WaitForCancellation = true };
        var model = new UpdateSettingsViewModel(store);
        var load = model.LoadAsync();
        model.Dispose();
        await load;
        Assert.False(model.CanSave);
        Assert.False(model.IsBusy);
        Assert.Equal(0, store.Saves);
    }
}