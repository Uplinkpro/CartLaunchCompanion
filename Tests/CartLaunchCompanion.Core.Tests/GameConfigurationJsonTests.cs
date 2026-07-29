using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GameConfigurationJsonTests
{
    [Fact]
    public void Serialize_WritesReadableGroupedConfiguration()
    {
        var configuration = new GameConfiguration
        {
            Game = { Name = "Portal 2" },
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
    }
}
