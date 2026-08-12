using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CollectionLayoutSaveServiceTests
{
    [Fact]
    public async Task SaveAsync_UpdatesCollectionAndEveryGamePlacement()
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
            var savedFirst = await GameConfigurationJson.LoadAsync(first);
            var savedSecond = await GameConfigurationJson.LoadAsync(second);
            Assert.Equal("New Era", Assert.Single(savedCollection.Shelves).Name);
            Assert.Equal(("New Era", 10), (savedFirst.Collection.Shelf, savedFirst.Collection.Order));
            Assert.Equal(("New Era", 20), (savedSecond.Collection.Shelf, savedSecond.Collection.Order));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_RestoresEarlierFilesWhenALaterWriteFails()
    {
        var root = CreateCart();
        try
        {
            var gamePath = await CreateGame(root, "One", "Original", 10);
            var originalCollection = new CollectionConfiguration { Enabled = true, Name = "Original" };
            await CollectionConfigurationJson.SaveAsync(Path.Combine(root, "Config"), originalCollection);
            Directory.CreateDirectory(gamePath + ".layout.tmp");

            await Assert.ThrowsAnyAsync<Exception>(() => new CollectionLayoutSaveService().SaveAsync(
                root,
                new CollectionConfiguration { Enabled = true, Name = "Changed" },
                [new(gamePath, "Changed", 20)]));

            Assert.Equal("Original", (await CollectionConfigurationJson.LoadAsync(Path.Combine(root, "Config"))).Name);
            Assert.Equal("Original", (await GameConfigurationJson.LoadAsync(gamePath)).Collection.Shelf);
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

    private static async Task<string> CreateGame(string root, string name, string shelf, int order)
    {
        var path = Path.Combine(root, "Games", name, "game.json");
        var game = new GameConfiguration { Game = { Name = name }, Collection = { Shelf = shelf, Order = order } };
        await GameConfigurationJson.SaveAsync(path, game);
        return path;
    }
}
