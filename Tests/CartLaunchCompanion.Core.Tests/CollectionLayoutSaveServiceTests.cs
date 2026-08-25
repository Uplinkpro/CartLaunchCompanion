using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CollectionLayoutSaveServiceTests
{
    [Fact]
    public async Task SaveAsync_StoresEveryGamePlacementInCollectionFileOnly()
    {
        var root = CreateCart();
        try
        {
            var first = await CreateGame(root, "One", "Old", 90);
            var second = await CreateGame(root, "Two", "", 0);
            var collection = new CollectionConfiguration
            {
                Enabled = true,
                Name = "Test Series",
                Shelves = [new CollectionShelfConfiguration { Name = "New Era", Order = 10 }]
            };

            await new CollectionLayoutSaveService().SaveAsync(root, collection,
            [
                new(first, "New Era", 10),
                new(second, "New Era", 20)
            ]);

            var savedCollection = await CollectionConfigurationJson.LoadAsync(Path.Combine(root, "Config"));
            Assert.Equal("New Era", Assert.Single(savedCollection.Shelves).Name);
            Assert.Collection(savedCollection.Placements,
                placement => Assert.Equal(("Games/One/game.json", "New Era", 10), (placement.Configuration, placement.Shelf, placement.Order)),
                placement => Assert.Equal(("Games/Two/game.json", "New Era", 20), (placement.Configuration, placement.Shelf, placement.Order)));
            Assert.All(savedCollection.Placements, placement => Assert.StartsWith("game-", placement.GameId));
            Assert.DoesNotContain("\"collection\"", await File.ReadAllTextAsync(first));
            Assert.DoesNotContain("\"collection\"", await File.ReadAllTextAsync(second));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_TreatsSameNamedGamesAtDifferentPathsAsSeparatePlacements()
    {
        var root = CreateCart();
        try
        {
            var pc = await CreateGame(root, "Grand Theft Auto - PC", "", 0, "Grand Theft Auto");
            var playStation = await CreateGame(root, "Grand Theft Auto - PlayStation", "", 0, "Grand Theft Auto");

            await new CollectionLayoutSaveService().SaveAsync(root, new CollectionConfiguration(),
            [
                new(pc, "PC", 10),
                new(playStation, "Console", 20)
            ]);

            var saved = await CollectionConfigurationJson.LoadAsync(Path.Combine(root, "Config"));
            Assert.Contains(saved.Placements, item => item.Configuration == "Games/Grand Theft Auto - PC/game.json" && item.Shelf == "PC");
            Assert.Contains(saved.Placements, item => item.Configuration == "Games/Grand Theft Auto - PlayStation/game.json" && item.Shelf == "Console");
            Assert.Equal(2, saved.Placements.Select(item => item.GameId).Distinct().Count());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_DoesNotModifyGameConfigurationFiles()
    {
        var root = CreateCart();
        try
        {
            var gamePath = await CreateGame(root, "One", "Original", 10);
            var before = await File.ReadAllBytesAsync(gamePath);
            await new CollectionLayoutSaveService().SaveAsync(root,
                new CollectionConfiguration { Enabled = true, Name = "Changed" },
                [new(gamePath, "Changed", 20)]);
            Assert.Equal(before, await File.ReadAllBytesAsync(gamePath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_RejectsGameConfigurationsOutsideCartGames()
    {
        var root = CreateCart();
        try
        {
            var outside = Path.Combine(root, "NotGames", "game.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
            await GameConfigurationJson.SaveAsync(outside, new GameConfiguration());
            await Assert.ThrowsAsync<InvalidOperationException>(() => new CollectionLayoutSaveService().SaveAsync(
                root, new CollectionConfiguration(), [new(outside, "Shelf", 10)]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateCart()
    {
        var root = Path.Combine(Path.GetTempPath(), "clc-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Games"));
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        return root;
    }

    private static async Task<string> CreateGame(string root, string name, string shelf, int order, string? displayName = null)
    {
        var path = Path.Combine(root, "Games", name, "game.json");
        var game = new GameConfiguration { Game = { Name = displayName ?? name } };
        await GameConfigurationJson.SaveAsync(path, game);
        return path;
    }
}
