using System.Net;
using System.Text;
using CartLaunchCompanion.Core.Metadata;

namespace CartLaunchCompanion.Core.Tests;

public sealed class SteamPlayerStatsClientTests
{
    [Fact]
    public async Task ReturnsAvailablePlaytimeAndAchievements()
    {
        using var httpClient = CreateClient(request =>
            request.RequestUri!.AbsolutePath.Contains("GetOwnedGames", StringComparison.Ordinal)
                ? Json("""
                       {"response":{"games":[{"appid":12210,"playtime_forever":2033,"rtime_last_played":1710000000}]}}
                       """)
                : Json("""
                       {"playerstats":{"success":true,"achievements":[{"achieved":1},{"achieved":0},{"achieved":1}]}}
                       """));

        var result = await new SteamPlayerStatsClient(httpClient)
            .GetAsync("secret", "76561198000000000", 12210);

        Assert.NotNull(result);
        Assert.Equal(2033, result.PlaytimeMinutes);
        Assert.NotNull(result.LastPlayed);
        Assert.Equal(2, result.EarnedAchievements);
        Assert.Equal(3, result.TotalAchievements);
    }

    [Fact]
    public async Task KeepsPlaytimeButOmitsUnavailableAchievements()
    {
        using var httpClient = CreateClient(request =>
            request.RequestUri!.AbsolutePath.Contains("GetOwnedGames", StringComparison.Ordinal)
                ? Json("""{"response":{"games":[{"appid":12210,"playtime_forever":45}]}}""")
                : new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await new SteamPlayerStatsClient(httpClient)
            .GetAsync("secret", "76561198000000000", 12210);

        Assert.NotNull(result);
        Assert.Equal(45, result.PlaytimeMinutes);
        Assert.Null(result.EarnedAchievements);
        Assert.Null(result.TotalAchievements);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new StubHandler(response));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
