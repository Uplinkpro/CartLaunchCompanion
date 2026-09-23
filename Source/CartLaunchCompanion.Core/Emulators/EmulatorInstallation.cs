using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>An installation on one platform, with a media-root-relative executable path.</summary>
public sealed record EmulatorInstallation
{
    public required string EmulatorId { get; init; }
    public required PlatformKind Platform { get; init; }
    public required string ExecutableRelativePath { get; init; }
    public string? InstalledVersion { get; init; }
    public string? InstalledChannelId { get; init; }
    public DateTimeOffset? InstalledAt { get; init; }
}
