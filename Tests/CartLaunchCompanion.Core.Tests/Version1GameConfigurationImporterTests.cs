using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Migration;

namespace CartLaunchCompanion.Core.Tests;

public sealed class Version1GameConfigurationImporterTests
{
    [Fact]
    public void Import_MapsKnownVersion1Fields()
    {
        const string json = """
        {
          "Name": "Portal 2",
          "Launcher": "Steam",
          "SteamID": "620",
          "SteamMetadataID": "620",
          "ProcessName": "portal2",
          "RestoreOnExit": true,
          "UnknownLegacyField": "keep track of me"
        }
        """;

        var importer = new Version1GameConfigurationImporter();

        var result = importer.Import(json);

        Assert.Equal("Portal 2", result.Configuration.Game.Name);
        Assert.Equal(
            LauncherKind.Steam,
            result.Configuration.Launch.Windows.Launcher);
        Assert.Equal("620", result.Configuration.Launch.Windows.SteamId);
        Assert.Equal(
            "620",
            result.Configuration.Artwork.SteamMetadataId);
        Assert.False(result.Configuration.Launch.Linux.Enabled);
        Assert.Contains("UnknownLegacyField", result.UnmappedFields);
    }

    [Fact]
    public void Import_DoesNotModifySourceJson()
    {
        const string json = """
        { "Name": "Example", "Launcher": "DirectExe", "Executable": "Game.exe" }
        """;

        var importer = new Version1GameConfigurationImporter();

        _ = importer.Import(json);

        Assert.Contains("\"Executable\": \"Game.exe\"", json);
    }
}
