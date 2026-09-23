using CartLaunchCompanion.Core.Emulators;
using Xunit;

namespace CartLaunchCompanion.EmulatorCompanion.Tests;

public sealed class AboutViewModelTests
{
    [Fact]
    public void BuildsThirdPartyAttributionFromCatalogDefinitions()
    {
        var model = new AboutViewModel([new EmulatorDefinition
        {
            Id = "sample", DisplayName = "Sample Emulator",
            Attribution = new()
            {
                WebsiteUrl = "https://example.com/",
                RepositoryUrl = "https://github.com/example/sample",
                License = "GPL-2.0-or-later",
                LicenseUrl = "https://example.com/license",
                Credits = "Created by Sample contributors.",
                BrandingSourceUrl = "https://example.com/icon.svg"
            }
        }]);

        var project = Assert.Single(model.Projects);
        Assert.True(model.HasProjects);
        Assert.Equal("Sample Emulator", project.Name);
        Assert.Equal("GPL-2.0-or-later", project.License);
        Assert.Equal("https://github.com/example/sample", project.Repository?.AbsoluteUri);
        Assert.StartsWith("Version 2.8.1", model.Version);
    }

    [Fact]
    public void OmitsEntriesWithoutAttributionAndReportsLinkFailures()
    {
        var model = new AboutViewModel([new EmulatorDefinition { Id = "sample", DisplayName = "Sample" }]);
        Assert.False(model.HasProjects);
        Assert.False(model.HasLinkError);
        model.ReportLinkFailure();
        Assert.True(model.HasLinkError);
    }
}
