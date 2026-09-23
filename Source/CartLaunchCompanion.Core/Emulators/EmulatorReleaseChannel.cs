using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Project-defined channel identity; names and IDs are not a fixed global enum.</summary>
public sealed record EmulatorReleaseChannel
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public bool IsStable { get; init; }
    // Empty means support is unknown, not that both platforms are supported.
    public IReadOnlyList<PlatformKind> SupportedPlatforms { get; init; } = [];
    public EmulatorReleaseSource? Source { get; init; }
}
