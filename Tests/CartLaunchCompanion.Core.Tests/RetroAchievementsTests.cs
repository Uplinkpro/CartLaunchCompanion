using System.Net;
using System.Security.Cryptography;
using System.Text;
using CartLaunchCompanion.Core.Metadata;

namespace CartLaunchCompanion.Core.Tests;

public sealed class RetroAchievementsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-RA-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GbaHashUsesWholeFileMd5()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "game.gba");
        var bytes = Encoding.ASCII.GetBytes("test-gba-rom");
        await File.WriteAllBytesAsync(path, bytes);

        var result = await new RetroAchievementsRomHasher().HashAsync(path, "Game Boy Advance");

        Assert.True(result.Supported);
        Assert.Equal(Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), result.Hash);
        Assert.Equal("Full-file MD5", result.Method);
    }

    [Fact]
    public async Task NesHashIgnoresInesHeader()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "game.nes");
        var payload = Encoding.ASCII.GetBytes("nes-payload");
        var bytes = new byte[16 + payload.Length];
        new byte[] { 0x4E, 0x45, 0x53, 0x1A }.CopyTo(bytes, 0);
        payload.CopyTo(bytes, 16);
        await File.WriteAllBytesAsync(path, bytes);

        var result = await new RetroAchievementsRomHasher().HashAsync(path, "Nintendo Entertainment System");

        Assert.True(result.Supported);
        Assert.Equal(Convert.ToHexString(MD5.HashData(payload)).ToLowerInvariant(), result.Hash);
        Assert.Equal("NES header removed", result.Method);
    }

    [Fact]
    public async Task DiscContainerRequiresAuthoritativeEmulatorHash()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "game.chd");
        await File.WriteAllTextAsync(path, "not-a-real-chd");

        var result = await new RetroAchievementsRomHasher().HashAsync(path, "PlayStation 2");

        Assert.False(result.Supported);
        Assert.Contains("rcheevos", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientMatchesPspAliasAndExactHash()
    {
        Directory.CreateDirectory(_root);
        const string hash = "8bd4a97783cda077c342173df0a9b51e";
        using var http = new HttpClient(new StubHandler(request =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("API_GetConsoleIDs.php", StringComparison.Ordinal)
                ? "[{\"ID\":41,\"Name\":\"PlayStation Portable\"}]"
                : $"[{{\"ID\":99,\"Title\":\"Test PSP Game\",\"ConsoleID\":41,\"ConsoleName\":\"PlayStation Portable\",\"ImageIcon\":\"/Images/test.png\",\"NumAchievements\":20,\"Points\":200,\"Hashes\":[\"{hash}\"]}}]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }));

        var match = await new RetroAchievementsClient(http).FindGameByHashAsync("PSP", hash, "key", _root);

        Assert.NotNull(match);
        Assert.Equal(99, match.GameId);
        Assert.Equal("Test PSP Game", match.Title);
        Assert.Equal(20, match.AchievementCount);
    }

    [Fact]
    public async Task ClientReadsUserProgressAndReturnsMostRecentUnlocks()
    {
        Directory.CreateDirectory(_root);
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "ID": 99,
              "Title": "Test Game",
              "NumAchievements": 3,
              "NumAwardedToUser": 2,
              "NumAwardedToUserHardcore": 1,
              "Achievements": {
                "10": { "ID": 10, "Title": "First", "Description": "One", "Points": 5, "BadgeName": "00010", "DateEarned": "2026-01-01 12:00:00" },
                "11": { "ID": 11, "Title": "Second", "Description": "Two", "Points": 10, "BadgeName": "00011", "DateEarned": "2026-01-02 12:00:00", "DateEarnedHardcore": "2026-01-03 12:00:00" },
                "12": { "ID": 12, "Title": "Locked", "Description": "Three", "Points": 25, "BadgeName": "00012" }
              }
            }
            """)
        }));

        var progress = await new RetroAchievementsClient(http).GetUserGameProgressAsync(
            "player", 99, "key", _root);

        Assert.Equal(3, progress.TotalAchievements);
        Assert.Equal(2, progress.EarnedAchievements);
        Assert.Equal(1, progress.EarnedHardcoreAchievements);
        Assert.Equal(15, progress.EarnedPoints);
        Assert.Equal("IN PROGRESS", progress.Award);
        Assert.Equal(["Second", "First"], progress.RecentAchievements.Select(item => item.Title));
        Assert.True(progress.RecentAchievements[0].EarnedHardcore);
    }

    [Fact]
    public void BadgeUrlAcceptsApiPathsAndNames()
    {
        Assert.Equal(
            "https://media.retroachievements.org/Badge/00010.png",
            RetroAchievementsClient.ResolveBadgeUrl("/Badge/00010.png"));
        Assert.Equal(
            "https://media.retroachievements.org/Badge/00011.png",
            RetroAchievementsClient.ResolveBadgeUrl("00011"));
        Assert.Equal(
            "https://cdn.example.test/badges/00013.png",
            RetroAchievementsClient.ResolveBadgeUrl("https://cdn.example.test/badges/00013.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
