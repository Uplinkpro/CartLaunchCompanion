using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorUpdateSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-update-settings-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task MissingPreferencesUseDefaultsWithoutCreatingFiles()
    {
        var store = new EmulatorUpdateSettingsStore(_root);
        Assert.Equal(new EmulatorUpdatePreferences(), await store.LoadAsync());
        Assert.Null(await store.GetSkippedVersionAsync(PlatformKind.Windows));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task PreferencesPersistAcrossInstances()
    {
        var expected = new EmulatorUpdatePreferences { CheckBeforeLaunch = false, CheckIntervalHours = 168 };
        await new EmulatorUpdateSettingsStore(_root).SaveAsync(expected);
        Assert.Equal(expected, await new EmulatorUpdateSettingsStore(_root).LoadAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(500)]
    public async Task InvalidIntervalDoesNotOverwriteSavedPreferences(int hours)
    {
        var store = new EmulatorUpdateSettingsStore(_root);
        await store.SaveAsync(new());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(new() { CheckIntervalHours = hours }));
        Assert.Equal(new EmulatorUpdatePreferences(), await store.LoadAsync());
    }

    [Fact]
    public async Task CorruptPreferencesCannotBeSilentlyReplaced()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Config"));
        var path = Path.Combine(_root, "Config", "emulator-update-preferences.json");
        await File.WriteAllTextAsync(path, "invalid");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new EmulatorUpdateSettingsStore(_root).SaveAsync(new()));
        Assert.Equal("invalid", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ClearOnlyChangesChosenPlatformSkip()
    {
        var cache = new PpssppUpdateStateStore(_root);
        var state = new PpssppUpdateState(CheckedAt: DateTimeOffset.UtcNow, RetryAfter: DateTimeOffset.UtcNow.AddMinutes(10), SkippedVersion: "v1.20.4");
        using (cache.Lock(PlatformKind.Windows)) await cache.SaveAsync(PlatformKind.Windows, state, default);
        using (cache.Lock(PlatformKind.Linux)) await cache.SaveAsync(PlatformKind.Linux, state, default);
        await new EmulatorUpdateSettingsStore(_root).ClearSkippedVersionAsync(PlatformKind.Windows);
        Assert.Equal(state with { SkippedVersion = null }, await cache.LoadAsync(PlatformKind.Windows, default));
        Assert.Equal(state, await cache.LoadAsync(PlatformKind.Linux, default));
    }

    [Fact]
    public async Task ClearRespectsActiveCacheWriterAndRetainsSkip()
    {
        var cache = new PpssppUpdateStateStore(_root);
        using var writer = cache.Lock(PlatformKind.Windows);
        await cache.SaveAsync(PlatformKind.Windows, new(SkippedVersion: "v1.20.4"), default);
        await Assert.ThrowsAsync<IOException>(() => new EmulatorUpdateSettingsStore(_root).ClearSkippedVersionAsync(PlatformKind.Windows));
        Assert.Equal("v1.20.4", (await cache.LoadAsync(PlatformKind.Windows, default)).SkippedVersion);
    }

    [Fact]
    public async Task CancelledSavePreservesPreviousPreferences()
    {
        var store = new EmulatorUpdateSettingsStore(_root);
        await store.SaveAsync(new());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new() { CheckBeforeLaunch = false }, cancelled.Token));
        Assert.True((await store.LoadAsync()).CheckBeforeLaunch);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}