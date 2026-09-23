namespace CartLaunchCompanion.Core.Emulators;

public enum EmulatorReleaseSourceKind { GitHubReleases, ProjectWebsite }
public enum EmulatorPrereleasePolicy { Any, Exclude, Only }

/// <summary>Discovery metadata only; not a download URL, asset selector, or executable instruction.</summary>
public sealed record EmulatorReleaseSource
{
    public required EmulatorReleaseSourceKind Kind { get; init; }
    public required string Url { get; init; }
    public EmulatorPrereleasePolicy Prereleases { get; init; }
    public string? Tag { get; init; }
    public string? TagPrefix { get; init; }
}
