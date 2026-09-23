using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Emulators.Adapters;
using CartLaunchCompanion.Core.Platform;
using Xunit;

namespace CartLaunchCompanion.EmulatorCompanion.Tests;

public sealed class BundledCatalogTests
{
    [Fact]
    public async Task BundledPpssppEntryTargetsOfficialStableReleases()
    {
        var catalog = await new EmulatorCatalogSource(Path.Combine(AppContext.BaseDirectory, "Catalog", "emulators.json")).LoadAsync();
        var ppsspp = Assert.Single(catalog.Emulators, emulator => emulator.Id == "ppsspp");
        Assert.Equal("ppsspp", ppsspp.Id);
        Assert.Equal(PpssppReleaseAdapter.RepositoryUrl, ppsspp.Attribution!.RepositoryUrl);
        var channel = Assert.Single(ppsspp.ReleaseChannels);
        Assert.True(channel.IsStable);
        Assert.Equal(PpssppReleaseAdapter.ChannelId, channel.Id);
        Assert.Null(channel.Source!.Tag);
        Assert.Equal("v", channel.Source.TagPrefix);
        Assert.Equal(EmulatorPrereleasePolicy.Exclude, channel.Source.Prereleases);
        Assert.Null(ppsspp.Branding);

        using var model = new ReleaseDetailsViewModel(ppsspp, new PpssppReleaseAdapter());
        model.SelectedPlatform = model.Platforms.Single(option => option.Platform == PlatformKind.Linux);
        Assert.True(model.CanCheck);
        model.SelectedPlatform = model.Platforms.Single(option => option.Platform is null);
        Assert.True(model.CanCheck);
    }

    [Fact]
    public async Task BundledDuckStationEntryUsesOfficialStableAndPreviewChannels()
    {
        var catalog = await new EmulatorCatalogSource(Path.Combine(AppContext.BaseDirectory, "Catalog", "emulators.json")).LoadAsync();
        var duckstation = Assert.Single(catalog.Emulators, emulator => emulator.Id == "duckstation");
        Assert.Equal(DuckStationReleaseAdapter.RepositoryUrl, duckstation.Attribution!.RepositoryUrl);
        Assert.Equal(DuckStationReleaseAdapter.StableChannelId, duckstation.DefaultChannelId);
        Assert.Collection(duckstation.ReleaseChannels,
            stable =>
            {
                Assert.True(stable.IsStable);
                Assert.Equal([PlatformKind.Windows, PlatformKind.Linux], stable.SupportedPlatforms);
                Assert.Equal("latest", stable.Source!.Tag);
                Assert.Equal(EmulatorPrereleasePolicy.Exclude, stable.Source.Prereleases);
            },
            preview =>
            {
                Assert.False(preview.IsStable);
                Assert.Equal([PlatformKind.Windows, PlatformKind.Linux], preview.SupportedPlatforms);
                Assert.Equal("preview", preview.Source!.Tag);
                Assert.Equal(EmulatorPrereleasePolicy.Only, preview.Source.Prereleases);
            });
    }
}
