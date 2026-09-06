using CartLaunchCompanion.Core.Tracking;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GamePlaytimeStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "clc-playtime-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TracksSessionsAndImportsSteamWithoutDoubleCounting()
    {
        var path = Path.Combine(_root, "Config", "playtime.json");
        var store = new GamePlaytimeStore(path);
        var firstPlayed = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        var imported = await store.ImportSteamAsync("game-one", 10, firstPlayed);
        Assert.Equal(600, imported.TotalSeconds);

        var tracked = await store.RecordSessionAsync(
            "game-one",
            TimeSpan.FromMinutes(1),
            firstPlayed.AddDays(1));
        Assert.Equal(660, tracked.TotalSeconds);

        var staleSteam = await store.ImportSteamAsync("game-one", 10, firstPlayed);
        Assert.Equal(660, staleSteam.TotalSeconds);
        Assert.Equal(firstPlayed.AddDays(1), staleSteam.LastPlayed);

        var refreshedSteam = await store.ImportSteamAsync(
            "game-one",
            12,
            firstPlayed.AddDays(2));
        Assert.Equal(720, refreshedSteam.TotalSeconds);
        Assert.Equal(firstPlayed.AddDays(2), refreshedSteam.LastPlayed);

        var persisted = await new GamePlaytimeStore(path).GetAsync("game-one");
        Assert.Equal(refreshedSteam, persisted);
    }

    [Fact]
    public async Task TracksNonSteamGameWithoutAnImport()
    {
        var path = Path.Combine(_root, "Config", "playtime.json");
        var store = new GamePlaytimeStore(path);
        var endedAt = DateTimeOffset.UtcNow;

        var tracked = await store.RecordSessionAsync(
            "game-emulated",
            TimeSpan.FromSeconds(95),
            endedAt);

        Assert.Equal(95, tracked.TotalSeconds);
        Assert.Equal(endedAt, tracked.LastPlayed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
