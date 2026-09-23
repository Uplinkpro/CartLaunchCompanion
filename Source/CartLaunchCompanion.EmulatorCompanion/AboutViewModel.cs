using System.ComponentModel;
using CartLaunchCompanion.Core.Emulators;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed record AboutProject
{
    public required string Name { get; init; }
    public required string Credits { get; init; }
    public required string License { get; init; }
    public Uri? Website { get; init; }
    public Uri? Repository { get; init; }
    public Uri? LicensePage { get; init; }
    public Uri? BrandingSource { get; init; }
    public bool HasWebsite => Website is not null;
    public bool HasRepository => Repository is not null;
    public bool HasLicensePage => LicensePage is not null;
    public bool HasBrandingSource => BrandingSource is not null;
}

public sealed class AboutViewModel : INotifyPropertyChanged
{
    public AboutViewModel(IEnumerable<EmulatorDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        Projects = definitions
            .Where(definition => definition.Attribution is not null)
            .OrderBy(definition => definition.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(definition =>
            {
                var attribution = definition.Attribution!;
                return new AboutProject
                {
                    Name = definition.DisplayName,
                    Credits = attribution.Credits ?? "See the project website for contributor information.",
                    License = attribution.License ?? "See the project website for license information.",
                    Website = ToUri(attribution.WebsiteUrl),
                    Repository = ToUri(attribution.RepositoryUrl),
                    LicensePage = ToUri(attribution.LicenseUrl),
                    BrandingSource = ToUri(attribution.BrandingSourceUrl)
                };
            })
            .ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string AppName => "Emulator Companion";
    public string Version => "Version " + FormatVersion(typeof(AboutViewModel).Assembly.GetName().Version);
    public string Author => "Created by Uplinkpro for Cart Launch Companion.";
    public Uri ProjectWebsite { get; } = new("https://github.com/Uplinkpro/CartLaunchCompanion");
    public Uri ReleasesPage { get; } = new("https://github.com/Uplinkpro/CartLaunchCompanion/releases");
    public Uri SupportPage { get; } = new("https://buymeacoffee.com/Uplinkpro");
    public IReadOnlyList<AboutProject> Projects { get; }
    public bool HasProjects => Projects.Count > 0;
    public string LinkError { get; private set; } = "";
    public bool HasLinkError => LinkError.Length > 0;

    public void ReportLinkFailure()
    {
        LinkError = "The link could not be opened. Check your default browser.";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static Uri? ToUri(string? value) => value is null ? null : new Uri(value, UriKind.Absolute);
    private static string FormatVersion(Version? version) => version is null ? "Unknown" :
        version.Revision > 0 ? version.ToString(4) : version.Build > 0 ? version.ToString(3) : version.ToString(2);
}
