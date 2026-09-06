using System.Net;
using System.Text;
using CartLaunchCompanion.Core.Metadata;

namespace CartLaunchCompanion.Core.Tests;

public sealed class ExophaseClientTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(),
        "CLC-ExophaseTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResolvesPlayerIdFromPublicProfilePage()
    {
        using var http = CreateClient(_ => Html(
            "<script>window.playerProfileId = 123456;</script>"));

        var playerId = await new ExophaseClient(http)
            .ResolvePlayerIdAsync("https://www.exophase.com/user/example/");

        Assert.Equal("123456", playerId);
    }

    [Fact]
    public async Task FindsExactTitleAndPreferredPlatform()
    {
        using var http = CreateClient(request =>
            request.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal)
                ? Json("""
                    {
                      "success": true,
                      "games": [
                        {
                          "meta": {"title":"Grand Theft Auto","platforms":[{"name":"PlayStation"}]},
                          "playtimeUnits":{"hours":2,"minutes":15},
                          "lastplayed_utc":1710000000,
                          "earned_awards":7,
                          "total_awards":20,
                          "percent":35
                        },
                        {
                          "meta": {"title":"Grand Theft Auto","platforms":[{"name":"Windows"}]},
                          "earned_awards":3,
                          "total_awards":10,
                          "percent":30
                        }
                      ]
                    }
                    """)
                : Json("""{"success":true,"games":[]}"""));

        var result = await new ExophaseClient(http).FindGameAsync(
            "123456", "Grand Theft Auto", "PlayStation", _cache);

        Assert.NotNull(result);
        Assert.Contains("PlayStation", result.Platforms);
        Assert.Equal(135, result.PlaytimeMinutes);
        Assert.Equal(7, result.EarnedAchievements);
        Assert.Equal(20, result.TotalAchievements);
        Assert.Equal(35d, result.CompletionPercent);
    }

    [Fact]
    public async Task UsesStaleCacheWhenRefreshFails()
    {
        var online = true;
        using var http = CreateClient(request =>
        {
            if (!online)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return request.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal)
                ? Json("""{"success":true,"games":[{"meta":{"title":"Test Game","platforms":[{"name":"GOG"}]},"earned_awards":1,"total_awards":4,"percent":25}]}""")
                : Json("""{"success":true,"games":[]}""");
        });
        var client = new ExophaseClient(http);
        var first = await client.GetGamesAsync("42", _cache);
        online = false;

        var cached = await client.GetGamesAsync("42", _cache);

        Assert.Single(first);
        Assert.Single(cached);
        Assert.Equal("Test Game", cached[0].Title);
    }

    [Fact]
    public async Task ForcedRefreshBypassesFreshCacheAndRequestsUncachedData()
    {
        var earned = 1;
        var requests = 0;
        using var http = CreateClient(request =>
        {
            requests++;
            return request.RequestUri!.Query.Contains("page=1", StringComparison.Ordinal)
                ? Json("""
                    {"success":true,"games":[{"meta":{"title":"Test Game","platforms":[{"name":"Windows"}]},"earned_awards":EARNED,"total_awards":4,"percent":25}]}
                    """.Replace("EARNED", earned.ToString()))
                : Json("""{"success":true,"games":[]}""");
        });
        var client = new ExophaseClient(http);

        var first = await client.GetGamesAsync("42", _cache, forceRefresh: true);
        earned = 2;
        var second = await client.GetGamesAsync("42", _cache, forceRefresh: true);

        Assert.Equal(1, Assert.Single(first).EarnedAchievements);
        Assert.Equal(2, Assert.Single(second).EarnedAchievements);
        Assert.Equal(4, requests);
    }

    [Fact]
    public async Task ForbiddenApiRequestWarmsPublicSiteAndRetriesOnce()
    {
        var apiAttempts = 0;
        var siteWarmups = 0;
        using var http = CreateClient(request =>
        {
            if (request.RequestUri!.Host.Equals("www.exophase.com", StringComparison.OrdinalIgnoreCase))
            {
                siteWarmups++;
                return Html("<html></html>");
            }

            apiAttempts++;
            if (apiAttempts == 1)
                return new HttpResponseMessage(HttpStatusCode.Forbidden);

            return request.RequestUri.Query.Contains("page=1", StringComparison.Ordinal)
                ? Json("""{"success":true,"games":[{"meta":{"title":"Test Game","platforms":[]}}]}""")
                : Json("""{"success":true,"games":[]}""");
        });

        var games = await new ExophaseClient(http).GetGamesAsync("42", _cache, forceRefresh: true);

        Assert.Single(games);
        Assert.Equal(1, siteWarmups);
        Assert.Equal(3, apiAttempts);
    }

    [Fact]
    public async Task CacheOnlyReadNeverMakesANetworkRequest()
    {
        var requests = 0;
        using var http = CreateClient(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

        var games = await new ExophaseClient(http).GetGamesAsync(
            "42", _cache, cacheOnly: true);

        Assert.Empty(games);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ImportsBrowserGamesWhenOptionalNumbersAreNull()
    {
        var count = await ExophaseClient.ImportBrowserGamesAsync(
            "123456",
            """
            [{
              "meta":{"title":"Grand Theft Auto V","platforms":[{"name":"Windows"}]},
              "playtimeUnits":{"hours":10,"minutes":null},
              "lastplayed_utc":null,
              "earned_awards":null,
              "total_awards":77,
              "percent":null
            }]
            """,
            _cache);

        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var games = await new ExophaseClient(http).GetGamesAsync("123456", _cache);

        Assert.Equal(1, count);
        var game = Assert.Single(games);
        Assert.Equal("Grand Theft Auto V", game.Title);
        Assert.Equal(600, game.PlaytimeMinutes);
        Assert.Null(game.LastPlayed);
        Assert.Equal(0, game.EarnedAchievements);
        Assert.Equal(77, game.TotalAchievements);
    }

    [Fact]
    public async Task ImportsIndividualEarnedAchievementsFromBrowserSync()
    {
        var count = await ExophaseClient.ImportBrowserGamesAsync(
            "123456",
            """
            [{
              "master_id":991,
              "meta":{"title":"Test Game","platforms":[{"name":"Epic"}]},
              "_clcGameId":12345,
              "earned_awards":1,
              "total_awards":10,
              "percent":10,
              "_earned":{"awards":[{
                "meta":{"title":"First Step","description":"Complete the introduction.","image":"/images/first.png"},
                "earned_utc":1710000000
              }]}
            }]
            """,
            _cache);

        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var game = Assert.Single(await new ExophaseClient(http).GetGamesAsync("123456", _cache));

        Assert.Equal(12345, game.GameId);
        var achievement = Assert.Single(game.RecentAchievements!);
        Assert.Equal("First Step", achievement.Title);
        Assert.Equal("Complete the introduction.", achievement.Description);
        Assert.Equal("/images/first.png", achievement.IconUrl);
        Assert.NotNull(achievement.EarnedAt);
    }

    [Fact]
    public async Task ImportBrowserGamesAsync_ReadsMasterIdFromGameList()
    {
        await ExophaseClient.ImportBrowserGamesAsync(
            "42",
            """[{"master_id":991,"meta":{"title":"Test Game","platforms":[]}}]""",
            _cache);

        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var games = await new ExophaseClient(http).GetGamesAsync("42", _cache);
        Assert.Equal(991, Assert.Single(games).GameId);
    }

    [Fact]
    public async Task ImportBrowserGamesAsync_PrefersPlayerSpecificGameId()
    {
        await ExophaseClient.ImportBrowserGamesAsync(
            "42",
            """[{"master_id":991,"master_playerid":12345,"meta":{"title":"Test Game","platforms":[]}}]""",
            _cache);

        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var games = await new ExophaseClient(http).GetGamesAsync("42", _cache);
        Assert.Equal(12345, Assert.Single(games).GameId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new StubHandler(response));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
