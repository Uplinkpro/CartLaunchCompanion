using System.Runtime.InteropServices;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public enum EmulatorPackageFormat { Zip, SevenZip, AppImage }

/// <summary>
/// A discovered package, not a downloaded or verified installation. RevisionId identifies
/// the upstream release/asset revision rather than relying on a potentially moving tag.
/// </summary>
public sealed record EmulatorRelease
{
    public required string EmulatorId { get; init; }
    public required string ChannelId { get; init; }
    public required PlatformKind Platform { get; init; }
    public required Architecture Architecture { get; init; }
    public required string Version { get; init; }
    public required string RevisionId { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required bool IsPrerelease { get; init; }
    public required Uri ReleasePage { get; init; }
    public required string AssetName { get; init; }
    public required Uri DownloadUrl { get; init; }
    public required long SizeBytes { get; init; }
    public required EmulatorPackageFormat PackageFormat { get; init; }
    public string? Sha256 { get; init; }
}

/// <summary>Read-only metadata discovery. Errors must not be presented as "no update".</summary>
public interface IEmulatorReleaseAdapter
{
    string EmulatorId { get; }

    /// <returns>The newest matching published package, or null if none match.
    /// Unsupported platforms/architectures and source failures throw explicitly.</returns>
    Task<EmulatorRelease?> FindLatestAsync(
        EmulatorReleaseChannel channel,
        PlatformKind platform,
        Architecture architecture,
        CancellationToken cancellationToken = default);
}
