using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Core.Library;

public sealed class GameLibraryEntry
{
    public required string FolderPath { get; init; }
    public required string ConfigurationPath { get; init; }
    public required GameConfiguration Configuration { get; init; }

    public string? CoverPath { get; init; }
    public string? HeroPath { get; init; }
    public string? BackgroundPath { get; init; }
    public string? LogoPath { get; init; }
    public string? IconPath { get; init; }
    public string? TrailerPath { get; init; }
    public string? TrailerSource { get; init; }
    public IReadOnlyList<string> ScreenshotPaths { get; init; } = [];

    public LaunchTargetSelection? LaunchTarget { get; init; }

    public List<ConfigurationValidationIssue> ValidationIssues { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool IsLaunchable =>
        LaunchTarget is { Enabled: true } &&
        ValidationIssues.All(
            issue => issue.Severity != ValidationSeverity.Error);
}
