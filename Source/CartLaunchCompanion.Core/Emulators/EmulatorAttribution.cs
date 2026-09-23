namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Optional catalog-owned information for a future About view.</summary>
public sealed record EmulatorAttribution
{
    public string? WebsiteUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? License { get; init; }
    public string? LicenseUrl { get; init; }
    public string? Credits { get; init; }
    public string? BrandingSourceUrl { get; init; }
}
