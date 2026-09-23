using System.Text.Json;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-emulator-tests-" + Guid.NewGuid().ToString("N"));
    private string RegistryPath => Path.Combine(_root, "Config", "emulator-registry.json");

    private static EmulatorInstallation Installation(PlatformKind platform = PlatformKind.Windows, string id = "sample") => new()
    {
        EmulatorId = id,
        Platform = platform,
        ExecutableRelativePath = EmulatorPathContract.ExecutablePath(platform, "Sample", "bin/sample.exe"),
        InstalledVersion = "release-1",
        InstalledChannelId = "stable",
        InstalledAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public async Task MissingRegistryReturnsEmptyWithoutCreatingDirectories()
    {
        var result = await new EmulatorRegistryStore(_root).LoadAsync();
        Assert.Equal(1, result.SchemaVersion);
        Assert.Empty(result.Installations);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task RoundTripUpdatesOnlyMatchingPlatformAndRemovesOnlyRequestedInstallation()
    {
        var store = new EmulatorRegistryStore(_root);
        await store.UpsertAsync(Installation());
        await store.UpsertAsync(Installation(PlatformKind.Linux));
        await store.UpsertAsync(Installation() with { InstalledVersion = "release-2" });
        var reloaded = await new EmulatorRegistryStore(_root).LoadAsync();
        Assert.Equal(2, reloaded.Installations.Count);
        Assert.Equal("release-2", reloaded.Installations.Single(i => i.Platform == PlatformKind.Windows).InstalledVersion);
        Assert.Equal(Installation(PlatformKind.Linux), reloaded.Installations.Single(i => i.Platform == PlatformKind.Linux));
        var json = await File.ReadAllTextAsync(RegistryPath);
        Assert.Contains("\"platform\": \"windows\"", json);
        Assert.DoesNotContain(_root, json);
        await store.RemoveAsync("sample", PlatformKind.Windows);
        Assert.Equal(PlatformKind.Linux, Assert.Single((await store.LoadAsync()).Installations).Platform);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RegistryPath)!, "*.tmp"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":2,\"installations\":[]}")]
    [InlineData("{\"installations\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"installations\":null}")]
    [InlineData("{\"schemaVersion\":1,\"installations\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"installations\":[],\"futureField\":true}")]
    public async Task InvalidOrFutureRegistryCannotBeOverwritten(string original)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        await File.WriteAllTextAsync(RegistryPath, original);
        var store = new EmulatorRegistryStore(_root);
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => store.UpsertAsync(Installation()));
        Assert.Equal(original, await File.ReadAllTextAsync(RegistryPath));
    }

    [Fact]
    public async Task WriterContentionFailsWithoutLosingState()
    {
        var store = new EmulatorRegistryStore(_root);
        await store.UpsertAsync(Installation());
        var original = await File.ReadAllTextAsync(RegistryPath);
        using (var writerLock = new FileStream(RegistryPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => new EmulatorRegistryStore(_root).UpsertAsync(Installation(PlatformKind.Linux)));
            Assert.Single((await store.LoadAsync()).Installations);
        }
        Assert.Equal(original, await File.ReadAllTextAsync(RegistryPath));
        await new EmulatorRegistryStore(_root).UpsertAsync(Installation(PlatformKind.Linux));
        Assert.Equal(2, (await store.LoadAsync()).Installations.Count);
    }

    [Fact]
    public async Task CancelledWritePreservesExistingRegistry()
    {
        var store = new EmulatorRegistryStore(_root);
        await store.UpsertAsync(Installation());
        var original = await File.ReadAllTextAsync(RegistryPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.UpsertAsync(Installation(PlatformKind.Linux), cancellation.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(RegistryPath));
    }

    [Fact]
    public void RejectsDuplicateInstallationKeysAndNumericPlatforms()
    {
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.WriteRegistry(new()
        {
            SchemaVersion = 1,
            Installations = [Installation(), Installation()]
        }));
        var json = EmulatorManagementJson.WriteRegistry(new() { SchemaVersion = 1, Installations = [Installation()] });
        Assert.Throws<JsonException>(() => EmulatorManagementJson.ReadRegistry(json.Replace("\"windows\"", "0")));
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.ValidateInstallation(
            Installation() with { Platform = PlatformKind.Unsupported }));
    }

    [Theory]
    [InlineData("../sample.exe")]
    [InlineData("/Emulators/Windows/Sample/sample.exe")]
    [InlineData("C:/Emulators/Windows/Sample/sample.exe")]
    [InlineData("Emulators/Windows/Sample/../sample.exe")]
    [InlineData("Emulators/Windows/Sample/sample.exe:stream")]
    [InlineData("Emulators/Windows/Sample/CON.exe")]
    [InlineData("Emulators/Windows/Sample/sample.exe.")]
    [InlineData("Emulators/Windows/Sample//sample.exe")]
    [InlineData("Emulators/Windows/Sample/./sample.exe")]
    [InlineData("Emulators\\Windows\\Sample\\sample.exe")]
    [InlineData("Emulators/Linux/Sample/sample.exe")]
    [InlineData("Emulators/Windows/sample.exe")]
    public void RejectsNonPortableOrMismatchedExecutablePaths(string path) =>
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.ValidateInstallation(
            Installation() with { ExecutableRelativePath = path }));

    [Fact]
    public void RelativePathResolvesAgainstRelocatedMediaRoot()
    {
        var relative = Installation().ExecutableRelativePath;
        var relocated = Path.Combine(_root, "relocated");
        Assert.Equal(Path.Combine(relocated, "Emulators", "Windows", "Sample", "bin", "sample.exe"),
            EmulatorPathContract.Resolve(relocated, relative));
        Assert.False(Directory.Exists(relocated));
    }

    [Fact]
    public void CatalogRoundTripAndValidation()
    {
        var definition = new EmulatorDefinition
        {
            Id = "sample",
            DisplayName = "Sample",
            SystemIds = ["test-system"],
            ReleaseChannels = [new() { Id = "project-nightly", DisplayName = "Nightly builds" }]
        };
        var catalog = new EmulatorCatalog { SchemaVersion = EmulatorCatalog.CurrentSchemaVersion, Emulators = [definition] };
        var json = EmulatorManagementJson.WriteCatalog(catalog);
        var reloaded = EmulatorManagementJson.ReadCatalog(json);
        Assert.Equal("project-nightly", Assert.Single(Assert.Single(reloaded.Emulators).ReleaseChannels).Id);
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.WriteCatalog(catalog with { SchemaVersion = 3 }));
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.WriteCatalog(catalog with { Emulators = [definition, definition] }));
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.WriteCatalog(catalog with
        {
            Emulators = [definition with { ReleaseChannels = [definition.ReleaseChannels[0], definition.ReleaseChannels[0]] }]
        }));
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.WriteCatalog(catalog with
        {
            Emulators = [definition with { Id = "Sample" }]
        }));
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.ReadCatalog(
            "{\"schemaVersion\":1,\"emulators\":[null]}"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
