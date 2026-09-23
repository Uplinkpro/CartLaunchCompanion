using System.Text.Json;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class EmulatorCatalogMetadataTests
{
    private static EmulatorDefinition Sample => new()
    {
        Id = "sample",
        DisplayName = "Sample project",
        DefaultChannelId = "stable",
        Branding = new()
        {
            IconRelativePath = "Assets/Emulators/Sample/icon.svg",
            LogoRelativePath = "Assets/Emulators/Sample/logo.png",
            ThemeColor = "#3A78FF",
            UsesOfficialBranding = true
        },
        Attribution = new()
        {
            WebsiteUrl = "https://example.com/",
            RepositoryUrl = "https://github.com/example/sample",
            License = "Example license",
            LicenseUrl = "https://example.com/license",
            Credits = "Example contributors",
            BrandingSourceUrl = "https://example.com/branding"
        },
        ReleaseChannels = [new()
        {
            Id = "stable",
            DisplayName = "Stable",
            Description = "Project's stable releases",
            IsStable = true,
            SupportedPlatforms = [PlatformKind.Windows, PlatformKind.Linux],
            Source = new()
            {
                Kind = EmulatorReleaseSourceKind.GitHubReleases,
                Url = "https://github.com/example/sample",
                Prereleases = EmulatorPrereleasePolicy.Exclude,
                TagPrefix = "v"
            }
        }]
    };

    private static EmulatorCatalog Catalog(EmulatorDefinition definition) =>
        new() { SchemaVersion = EmulatorCatalog.CurrentSchemaVersion, Emulators = [definition] };

    private static string Write(EmulatorDefinition definition) => EmulatorManagementJson.WriteCatalog(Catalog(definition));

    [Fact]
    public void MetadataRoundTripsWithExplicitVersionAndStringEnums()
    {
        var json = Write(Sample);
        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.Contains("\"kind\": \"gitHubReleases\"", json);
        Assert.Contains("\"prereleases\": \"exclude\"", json);
        var result = Assert.Single(EmulatorManagementJson.ReadCatalog(json).Emulators);
        Assert.Equal(Sample.Branding, result.Branding);
        Assert.Equal(Sample.Attribution, result.Attribution);
        Assert.Equal(Sample.ReleaseChannels[0].Source, result.ReleaseChannels[0].Source);
        Assert.Equal(Sample.ReleaseChannels[0].SupportedPlatforms, result.ReleaseChannels[0].SupportedPlatforms);
        Assert.Equal("stable", result.DefaultChannelId);
    }

    [Fact]
    public void LegacyCatalogIsUpgradedInMemoryAndWrittenAsVersionTwo()
    {
        const string original = """
            {"schemaVersion":1,"emulators":[{"id":"sample","displayName":"Sample",
            "releaseChannels":[{"id":"nightly","displayName":"Nightly"}]}]}
            """;
        var catalog = EmulatorManagementJson.ReadCatalog(original);
        Assert.Equal(2, catalog.SchemaVersion);
        var entry = Assert.Single(catalog.Emulators);
        Assert.Null(entry.Branding);
        Assert.Null(entry.Attribution);
        Assert.Null(entry.DefaultChannelId);
        Assert.Empty(entry.ReleaseChannels[0].SupportedPlatforms);
        Assert.Null(entry.ReleaseChannels[0].Source);
        Assert.Contains("\"schemaVersion\": 2", EmulatorManagementJson.WriteCatalog(catalog));
    }

    [Theory]
    [InlineData("branding", "{}")]
    [InlineData("attribution", "null")]
    [InlineData("defaultChannelId", "null")]
    public void VersionOneCannotSmuggleExtendedFields(string field, string value)
    {
        var json = $$"""{"schemaVersion":1,"emulators":[{"id":"sample","displayName":"Sample","{{field}}":{{value}}}]}""";
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.ReadCatalog(json));
    }

    [Fact]
    public void VersionOneChannelsRemainStrict()
    {
        Assert.Throws<InvalidDataException>(() => EmulatorManagementJson.ReadCatalog("""
            {"schemaVersion":1,"emulators":[{"id":"sample","displayName":"Sample",
            "releaseChannels":[{"id":"stable","displayName":"Stable","isStable":true}]}]}
            """));
    }

    [Theory]
    [InlineData("https://example.com/icon.png")]
    [InlineData("../icon.png")]
    [InlineData("Assets/Emulators/Sample/../../icon.png")]
    [InlineData("Assets/Emulators/Sample/icon.exe")]
    [InlineData("Assets/Other/icon.png")]
    [InlineData("Assets\\Emulators\\Sample\\icon.png")]
    public void RejectsNonLocalOrUnsupportedArtwork(string path) =>
        Assert.Throws<InvalidDataException>(() => Write(Sample with { Branding = new() { IconRelativePath = path } }));

    [Theory]
    [InlineData("blue")]
    [InlineData("#FFF")]
    [InlineData("#00GG00")]
    [InlineData("#FF000000")]
    public void RejectsInvalidThemeColor(string color) =>
        Assert.Throws<InvalidDataException>(() => Write(Sample with { Branding = new() { ThemeColor = color } }));

    [Fact]
    public void OfficialBrandingRequiresSourceAttribution() =>
        Assert.Throws<InvalidDataException>(() => Write(Sample with { Attribution = null }));

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///C:/license.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com")]
    [InlineData(" https://example.com")]
    [InlineData("https://example.com/a b")]
    public void RejectsInvalidAttributionLinks(string url) =>
        Assert.Throws<InvalidDataException>(() => Write(Sample with
        {
            Attribution = Sample.Attribution! with { WebsiteUrl = url }
        }));

    [Fact]
    public void DefaultChannelMustBelongToSameEmulator() =>
        Assert.Throws<InvalidDataException>(() => Write(Sample with { DefaultChannelId = "missing" }));

    [Fact]
    public void UnknownPlatformsRemainAllowedOnlyWithoutReleaseSource()
    {
        Assert.Throws<InvalidDataException>(() => Write(Sample with
        {
            ReleaseChannels = [Sample.ReleaseChannels[0] with { SupportedPlatforms = [] }]
        }));
        Write(Sample with
        {
            ReleaseChannels = [Sample.ReleaseChannels[0] with { SupportedPlatforms = [], Source = null }]
        });
    }

    [Theory]
    [InlineData(PlatformKind.Unsupported)]
    [InlineData((PlatformKind)99)]
    public void InvalidPlatformsAreRejected(PlatformKind platform) =>
        Assert.Throws<InvalidDataException>(() => Write(Sample with
        {
            ReleaseChannels = [Sample.ReleaseChannels[0] with { SupportedPlatforms = [platform] }]
        }));

    [Fact]
    public void DuplicatePlatformsAreRejected() =>
        Assert.Throws<InvalidDataException>(() => Write(Sample with
        {
            ReleaseChannels = [Sample.ReleaseChannels[0] with { SupportedPlatforms = [PlatformKind.Windows, PlatformKind.Windows] }]
        }));

    [Theory]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("https://github.com/example/sample/releases")]
    [InlineData("https://github.com/example/sample?filter=nightly")]
    [InlineData("https://github.com/example/sample#latest")]
    [InlineData("https://github.com/example")]
    [InlineData("https://github.com/example/../sample")]
    [InlineData("https://github.com//example/sample")]
    public void GitHubSourcesRequireRepositoryUrl(string url) =>
        Assert.Throws<InvalidDataException>(() => WriteWithSource(Sample.ReleaseChannels[0].Source! with { Url = url }));

    [Fact]
    public void ExactTagAndPrefixCannotBeCombined() =>
        Assert.Throws<InvalidDataException>(() => WriteWithSource(Sample.ReleaseChannels[0].Source! with { Tag = "nightly" }));

    [Fact]
    public void WebsiteSourcesDoNotAcceptGithubFilters()
    {
        var source = new EmulatorReleaseSource { Kind = EmulatorReleaseSourceKind.ProjectWebsite, Url = "https://example.com/releases" };
        WriteWithSource(source);
        Assert.Throws<InvalidDataException>(() => WriteWithSource(source with { Prereleases = EmulatorPrereleasePolicy.Only }));
        Assert.Throws<InvalidDataException>(() => WriteWithSource(source with { Tag = "nightly" }));
    }

    [Fact]
    public void UnknownAndNumericEnumValuesAreRejected()
    {
        var json = Write(Sample);
        Assert.Throws<JsonException>(() => EmulatorManagementJson.ReadCatalog(json.Replace("\"gitHubReleases\"", "0")));
        Assert.Throws<JsonException>(() => EmulatorManagementJson.ReadCatalog(json.Replace("\"exclude\"", "\"future\"")));
        Assert.Throws<InvalidDataException>(() => WriteWithSource(Sample.ReleaseChannels[0].Source! with
        {
            Kind = (EmulatorReleaseSourceKind)99
        }));
    }

    [Fact]
    public void RegistryVersionAndShapeAreUnchanged()
    {
        var json = EmulatorManagementJson.WriteRegistry(EmulatorRegistry.Empty);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.DoesNotContain("branding", json);
    }

    private static void WriteWithSource(EmulatorReleaseSource source) => Write(Sample with
    {
        ReleaseChannels = [Sample.ReleaseChannels[0] with { Source = source }]
    });
}
