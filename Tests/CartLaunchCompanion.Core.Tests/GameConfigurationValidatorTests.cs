using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Validation;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GameConfigurationValidatorTests
{
    private readonly GameConfigurationValidator _validator = new();

    [Fact]
    public void Validate_RejectsMissingName()
    {
        var configuration = CreateValidSteamConfiguration();
        configuration.Game.Name = "";

        var result = _validator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            issue => issue.Path == "game.name");
    }

    [Fact]
    public void Validate_AcceptsCrossPlatformSteamGame()
    {
        var configuration = CreateValidSteamConfiguration();

        var result = _validator.Validate(configuration);

        Assert.True(result.IsValid);
    }

    private static GameConfiguration CreateValidSteamConfiguration()
        => new()
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
}
