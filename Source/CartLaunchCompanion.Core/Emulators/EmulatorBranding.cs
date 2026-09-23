namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Artwork paths are relative to the catalog directory, not the media root.</summary>
public sealed record EmulatorBranding
{
    public string? IconRelativePath { get; init; }
    public string? LogoRelativePath { get; init; }
    public string? BannerRelativePath { get; init; }
    public string? ThemeColor { get; init; }
    public bool UsesOfficialBranding { get; init; }
}
