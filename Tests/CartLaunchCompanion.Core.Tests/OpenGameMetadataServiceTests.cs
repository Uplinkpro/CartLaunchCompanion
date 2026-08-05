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
}
