using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GameConfigurationJsonTests
{
    [Fact]
    public void Serialize_WritesReadableGroupedConfiguration()
    {
        var configuration = new GameConfiguration
        {
            Game =
            {
                Name = "Portal 2",
                SteamDeckCompatibility = SteamDeckCompatibility.Verified
            },
            Launch =
            {
                Windows =
                {
                    Launcher = LauncherKind.Steam,
                    SteamId = "620"
                },
                Linux =
                {
                    Launcher = LauncherKind.Steam,
                    SteamId = "620"
                }
            }
        };

        var json = GameConfigurationJson.Serialize(configuration);

        Assert.Contains("\"formatVersion\": 2", json);
        Assert.Contains("\"game\": {", json);
        Assert.Contains("\"preferredPlatform\": \"automatic\"", json);
        Assert.Contains("\"launcher\": \"steam\"", json);
        Assert.Contains("\"steamDeckCompatibility\": \"verified\"", json);
        Assert.Contains("\"collection\": {", json);
    }

    [Fact]
    public void Deserialize_AllowsCaseInsensitivePropertyNames()
    {
        const string json = """
        {
          "FORMATVERSION": 2,
          "GAME": { "NAME": "Portal 2" },
          "ARTWORK": {},
          "LAUNCH": {
            "PREFERREDPLATFORM": "automatic",
            "WINDOWS": {
              "ENABLED": true,
              "LAUNCHER": "steam",
              "STEAMID": "620"
            },
            "LINUX": {
              "ENABLED": true,
              "LAUNCHER": "steam",
              "STEAMID": "620"
            }
          },
          "BEHAVIOR": {},
          "NOTES": ""
        }
        """;

        var configuration = GameConfigurationJson.Deserialize(json);

        Assert.Equal("Portal 2", configuration.Game.Name);
        Assert.Equal("620", configuration.Launch.Windows.SteamId);
        Assert.Equal("", configuration.Collection.Shelf);
    }

    [Fact]
    public async Task CollectionConfiguration_LoadsOptionalCartDefinition()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            $"clc-collection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(folder, "collection.json"),
                """
                {
                  "formatVersion": 1,
                  "enabled": true,
                  "name": "GTA Master Collection",
                  "defaultShelf": "Other",
                  "shelves": [{ "name": "3D Era", "order": 20 }]
                }
                """);

            var collection = await CollectionConfigurationJson.LoadAsync(folder);

            Assert.True(collection.Enabled);
            Assert.Equal("GTA Master Collection", collection.Name);
            Assert.Equal("3D Era", Assert.Single(collection.Shelves).Name);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task CollectionConfiguration_DefaultShelf_IsUnnamed()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            $"clc-collection-{Guid.NewGuid():N}");

        var collection = await CollectionConfigurationJson.LoadAsync(folder);

        Assert.Equal("", collection.DefaultShelf);
    }
}
