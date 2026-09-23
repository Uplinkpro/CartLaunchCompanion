using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-library-" + Guid.NewGuid().ToString("N"));
    private string CatalogPath => Path.Combine(_root, "catalog.json");

    private EmulatorLibraryService Service() => new(new EmulatorCatalogSource(CatalogPath), new EmulatorRegistryStore(_root));

    private async Task WriteCatalog(params EmulatorDefinition[] entries)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(CatalogPath, EmulatorManagementJson.WriteCatalog(new() { SchemaVersion = EmulatorCatalog.CurrentSchemaVersion, Emulators = entries }));
    }

    private static EmulatorDefinition Definition(string id, string name) => new() { Id = id, DisplayName = name };
    private static EmulatorInstallation Installation(string id, PlatformKind platform) => new()
    {
        EmulatorId = id, Platform = platform,
        ExecutableRelativePath = EmulatorPathContract.ExecutablePath(platform, id, "emulator")
    };

    [Fact]
    public async Task EmptyCatalogAndMissingRegistryAreAnEmptyLibraryWithoutWrites()
    {
        await WriteCatalog();
        var snapshot = await Service().LoadAsync();
        Assert.Empty(snapshot.Entries);
        Assert.False(snapshot.CatalogUnavailable);
        Assert.False(snapshot.RegistryUnavailable);
        Assert.False(Directory.Exists(Path.Combine(_root, "Config")));
    }

    [Fact]
    public async Task JoinsBothPlatformsRetainsUnknownEntriesAndIncludesUninstalledDefinitions()
    {
        await WriteCatalog(Definition("sample", "Alpha"), Definition("available", "Beta"));
        var store = new EmulatorRegistryStore(_root);
        await store.UpsertAsync(Installation("sample", PlatformKind.Windows));
        await store.UpsertAsync(Installation("sample", PlatformKind.Linux));
        await store.UpsertAsync(Installation("orphan", PlatformKind.Linux));
        var snapshot = await Service().LoadAsync();
        Assert.Equal(new[] { "sample", "available", "orphan" }, snapshot.Entries.Select(item => item.EmulatorId));
        Assert.Equal(2, snapshot.Entries.Single(item => item.EmulatorId == "sample").Installations.Count);
        Assert.Empty(snapshot.Entries.Single(item => item.EmulatorId == "available").Installations);
        Assert.Null(snapshot.Entries.Single(item => item.EmulatorId == "orphan").Definition);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":3,\"emulators\":[]}")]
    public async Task InvalidCatalogStillShowsRecordedInstallations(string json)
    {
        await WriteCatalog();
        await File.WriteAllTextAsync(CatalogPath, json);
        await new EmulatorRegistryStore(_root).UpsertAsync(Installation("sample", PlatformKind.Windows));
        var snapshot = await Service().LoadAsync();
        Assert.True(snapshot.CatalogUnavailable);
        Assert.False(snapshot.RegistryUnavailable);
        Assert.Single(Assert.Single(snapshot.Entries).Installations);
    }

    [Fact]
    public async Task MissingCatalogIsAnErrorNotAnEmptyCatalog()
    {
        var result = await Service().LoadAsync();
        Assert.True(result.CatalogUnavailable);
        Assert.False(result.RegistryUnavailable);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task InvalidRegistryFlagsUnknownInstallationStateWithoutOverwriting()
    {
        await WriteCatalog(Definition("sample", "Sample"));
        Directory.CreateDirectory(Path.Combine(_root, "Config"));
        var path = Path.Combine(_root, "Config", "emulator-registry.json");
        await File.WriteAllTextAsync(path, "{");
        var snapshot = await Service().LoadAsync();
        Assert.True(snapshot.RegistryUnavailable);
        Assert.False(snapshot.CatalogUnavailable);
        Assert.NotNull(Assert.Single(snapshot.Entries).Definition);
        Assert.Equal("{", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RefreshRereadsFilesAndCancellationPropagates()
    {
        await WriteCatalog();
        var service = Service();
        Assert.Empty((await service.LoadAsync()).Entries);
        await WriteCatalog(Definition("added", "Added"));
        Assert.Single((await service.LoadAsync()).Entries);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(cancellation.Token));
    }

    [Fact]
    public void ReadOnlyDiscoveryUsesSameRootWithoutCreatingWritableFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Games"));
        var nested = Path.Combine(_root, "System", "Companion");
        Directory.CreateDirectory(nested);
        var service = new PortablePathService();
        Assert.Equal(_root, service.DiscoverReadOnly(nested).Root);
        Assert.False(Directory.Exists(Path.Combine(_root, "Config")));
        Assert.Equal(_root, service.Discover(nested).Root);
        Assert.True(Directory.Exists(Path.Combine(_root, "Config")));
        Assert.True(Directory.Exists(Path.Combine(_root, "Logs")));
        Assert.True(Directory.Exists(Path.Combine(_root, "System", "Cache")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
