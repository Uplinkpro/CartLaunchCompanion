using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartWriteInterruptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-WriteTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GameJson_CancelledSavePreservesExistingFileAndCleansTemporaryFile()
    {
        var path = Path.Combine(_root, "Games", "Test", "game.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "known-valid-content");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GameConfigurationJson.SaveAsync(path, new GameConfiguration { Game = new() { Name = "Replacement" } }, cancelled.Token));

        Assert.Equal("known-valid-content", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task CollectionJson_CancelledSavePreservesExistingFileAndCleansTemporaryFile()
    {
        var folder = Path.Combine(_root, "Config");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "collection.json");
        await File.WriteAllTextAsync(path, "known-valid-content");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CollectionConfigurationJson.SaveAsync(folder, new CollectionConfiguration { Name = "Replacement" }, cancelled.Token));

        Assert.Equal("known-valid-content", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task CollectionLayout_CancelledTransactionPreservesEveryExistingFile()
    {
        var gameFolder = Path.Combine(_root, "Games", "Test");
        var gamePath = Path.Combine(gameFolder, "game.json");
        var configFolder = Path.Combine(_root, "Config");
        Directory.CreateDirectory(gameFolder); Directory.CreateDirectory(configFolder);
        var game = new GameConfiguration { Game = new() { Name = "Original" } };
        await GameConfigurationJson.SaveAsync(gamePath, game);
        var originalGame = await File.ReadAllBytesAsync(gamePath);
        var collectionPath = Path.Combine(configFolder, "collection.json");
        await File.WriteAllTextAsync(collectionPath, "original-collection");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CollectionLayoutSaveService().SaveAsync(
            _root, new CollectionConfiguration { Name = "New" },
            [new CollectionGamePlacementUpdate(gamePath, "Shelf", 1)], cancelled.Token));

        Assert.Equal(originalGame, await File.ReadAllBytesAsync(gamePath));
        Assert.Equal("original-collection", await File.ReadAllTextAsync(collectionPath));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.layout.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Identity_CancelledCreationLeavesNoIdentityOrTemporaryFile()
    {
        Directory.CreateDirectory(_root);
        var service = new CartIdentityService();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveNewAsync(_root, service.Create("Interrupted cart"), cancelled.Token));

        Assert.False(File.Exists(CartIdentityService.GetIdentityPath(_root)));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(_root, CartIdentityService.DirectoryName), CartIdentityService.FileName + ".tmp-*"));
    }

    [Fact]
    public async Task SuccessfulAtomicSavesRemainReadable()
    {
        var gamePath = Path.Combine(_root, "Games", "Test", "game.json");
        await GameConfigurationJson.SaveAsync(gamePath, new GameConfiguration { Game = new() { Name = "Readable Game" } });
        var configFolder = Path.Combine(_root, "Config");
        await CollectionConfigurationJson.SaveAsync(configFolder, new CollectionConfiguration { Name = "Readable Collection" });

        Assert.Equal("Readable Game", (await GameConfigurationJson.LoadAsync(gamePath)).Game.Name);
        Assert.Equal("Readable Collection", (await CollectionConfigurationJson.LoadAsync(configFolder)).Name);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
