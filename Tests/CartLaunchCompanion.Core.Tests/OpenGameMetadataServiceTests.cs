using System.Net;
using System.Text;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Metadata;

namespace CartLaunchCompanion.Core.Tests;

public sealed class OpenGameMetadataServiceTests
{
    [Fact]
    public async Task FillsDelistedGameMetadataWithoutCredentials()
    {
        using var client = new HttpClient(new MetadataHandler());
        var configuration = new GameConfiguration();
        configuration.Game.Name = "Grand Theft Auto";

        var result = await new OpenGameMetadataService(client)
            .FillMissingAsync(configuration, "12170");

        Assert.True(result.UsedPcGamingWiki);
        Assert.True(result.UsedWikipedia);
        Assert.Equal("DMA Design, Rockstar Canada", configuration.Game.Developer);
        Assert.Equal("BMG Interactive", configuration.Game.Publisher);
        Assert.Equal("Action, Shooter", configuration.Game.Genre);
        Assert.Equal("1997-10-21", configuration.Game.ReleaseDate);
        Assert.StartsWith("Grand Theft Auto is", configuration.Game.Description);
    }

    [Fact]
    public async Task FillsNonSteamGameMetadataByTitle()
    {
        using var client = new HttpClient(new TitleMetadataHandler());
        var configuration = new GameConfiguration();
        configuration.Game.Name = "Grand Theft Auto: London 1969";

        var result = await new OpenGameMetadataService(client)
            .FillMissingAsync(configuration);

        Assert.True(result.UsedPcGamingWiki);
        Assert.True(result.UsedWikipedia);
        Assert.Equal("Rockstar Canada", configuration.Game.Developer);
        Assert.Equal("Rockstar Games", configuration.Game.Publisher);
        Assert.Equal("Action", configuration.Game.Genre);
        Assert.Equal("1999-04-30", configuration.Game.ReleaseDate);
        Assert.StartsWith("Grand Theft Auto: London 1969 is", configuration.Game.Description);
    }

    private sealed class MetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.Host.Contains("pcgamingwiki", StringComparison.OrdinalIgnoreCase)
                ? """{"cargoquery":[{"title":{"Page":"Grand Theft Auto","Developers":"Company:DMA Design,Company:Rockstar Canada","Publishers":"Company:BMG Interactive","Released":"1997-10-21;1997-10-21","Genres":"Action,Shooter,"}}]}"""
                : """{"query":{"pages":{"123":{"title":"Grand Theft Auto (video game)","extract":"Grand Theft Auto is a 1997 action-adventure game."}}}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TitleMetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var json = uri.Host.Contains("wikipedia", StringComparison.OrdinalIgnoreCase)
                ? """{"query":{"pages":{"123":{"title":"Grand Theft Auto: London 1969","extract":"Grand Theft Auto: London 1969 is an expansion pack for Grand Theft Auto.","pageprops":{"wikibase_item":"Q19431"}}}}}"""
                : uri.Host.Contains("wikidata", StringComparison.OrdinalIgnoreCase) && uri.Query.Contains("props=claims", StringComparison.Ordinal)
                    ? """{"entities":{"Q19431":{"claims":{"P178":[{"mainsnak":{"datavalue":{"value":{"id":"QDEV"}}}}],"P123":[{"mainsnak":{"datavalue":{"value":{"id":"QPUB"}}}}],"P136":[{"mainsnak":{"datavalue":{"value":{"id":"QGENRE"}}}}],"P577":[{"mainsnak":{"datavalue":{"value":{"time":"+1999-04-30T00:00:00Z"}}}}]}}}}"""
                : uri.Host.Contains("wikidata", StringComparison.OrdinalIgnoreCase)
                    ? """{"entities":{"QDEV":{"labels":{"en":{"value":"Rockstar Canada"}}},"QPUB":{"labels":{"en":{"value":"Rockstar Games"}}},"QGENRE":{"labels":{"en":{"value":"Action"}}}}}"""
                : uri.Query.Contains("list=search", StringComparison.Ordinal)
                    ? """{"query":{"search":[{"title":"Grand Theft Auto: London 1969"}]}}"""
                    : """{"cargoquery":[{"title":{"Page":"Grand Theft Auto: London 1969","Developers":"Company:Rockstar Canada","Publishers":"Company:Rockstar Games","Released":"1999-04-30","Genres":"Action,","Cover URL":"https://example.test/london.jpg"}}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
