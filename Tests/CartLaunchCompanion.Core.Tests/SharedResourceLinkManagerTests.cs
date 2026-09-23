using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class SharedResourceLinkManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-shared-link-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PreviewDescribesNewAndMigratedResourceFolders()
    {
        var manager = new SharedResourceLinkManager(_root, _root);
        var psp = Path.Combine(_root, "Emulators", "Windows", "PPSSPP", "memstick", "PSP");
        var savedata = Path.Combine(psp, "SAVEDATA");
        var states = Path.Combine(psp, "PPSSPP_STATE");
        Directory.CreateDirectory(savedata);
        File.WriteAllText(Path.Combine(savedata, "PARAM.SFO"), "fixture");

        var changes = manager.Preview(
        [
            new(savedata, Path.Combine(_root, "Emulators", "Shared", "Saves", "PPSSPP"), "PPSSPP saved games"),
            new(states, Path.Combine(_root, "Emulators", "Shared", "States", "PPSSPP"), "PPSSPP save states")
        ]);

        Assert.Contains(changes, change => change.StartsWith("Move existing PPSSPP saved games", StringComparison.Ordinal));
        Assert.Contains(changes, change => change.StartsWith("Create the shared PPSSPP save states", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MigrationCopiesDataAndRetainsOriginalFolderAsBackup()
    {
        var source = Path.Combine(_root, "Emulators", "Windows", "DuckStation", "memcards");
        var target = Path.Combine(_root, "Emulators", "Shared", "Saves", "DuckStation");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "card.mcd"), "save data");
        var manager = new SharedResourceLinkManager(_root, _root);

        await manager.MigrateAsync([new(source, target, "DuckStation memory cards")]);

        Assert.Equal("save data", File.ReadAllText(Path.Combine(target, "card.mcd")));
        Assert.False(Directory.Exists(source));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "Config", "EmulatorCompanion", "Backups", "SharedResources"),
            "card.mcd", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
