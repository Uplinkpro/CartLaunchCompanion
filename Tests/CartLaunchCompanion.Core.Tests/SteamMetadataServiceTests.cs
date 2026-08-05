using System.Net;
using System.Text;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Metadata;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class SteamMetadataServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CLC-MetadataTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnrichAsync_FillsBlankSteamMetadata_ButPreservesOverrides()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.Host == "store.steampowered.com")
            {
                return JsonResponse(
                    """
                    {
                      "12170": {
                        "success": true,
                        "data": {
                          "short_description": "Steam description",
                          "developers": ["Steam developer"],
                          "publishers": ["Steam publisher"],
                          "genres": [{"description": "Action"}],
                          "categories": [{"description": "Full controller support"}],
                          "release_date": {"date": "1997"}
                        }
                      }
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var configuration = CreateConfiguration("12170");
        configuration.Game.Developer = "Local developer";
        configuration.Artwork.DownloadMissingArtwork = false;

        var paths = CreatePaths();
        var gameFolder = CreateGameFolder(paths, "Grand Theft Auto");
        var service = CreateService(handler);

        await service.EnrichAsync(gameFolder, configuration, paths);

        Assert.Equal("Steam description", configuration.Game.Description);
        Assert.Equal("Local developer", configuration.Game.Developer);
        Assert.Equal("Steam publisher", configuration.Game.Publisher);
        Assert.Equal("Action", configuration.Game.Genre);
        Assert.Equal("1997", configuration.Game.ReleaseDate);
        Assert.Equal(GamepadSupport.Full, configuration.Game.GamepadSupport);
    }

    [Fact]
    public async Task EnrichAsync_DoesNotReplaceExistingLocalArtwork()
    {
        var requests = new List<Uri>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return JsonResponse(
                """{"12170":{"success":true,"data":{}}}""");
        });

        var paths = CreatePaths();
        var gameFolder = CreateGameFolder(paths, "Local Artwork");
        var configuration = CreateConfiguration("12170");

        foreach (var relativePath in new[]
                 {
                     configuration.Artwork.Cover,
                     configuration.Artwork.Background,
                     configuration.Artwork.Logo,
                     configuration.Artwork.Icon
                 })
        {
            var fullPath = Path.Combine(
                gameFolder,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, [1, 2, 3]);
        }

        await CreateService(handler).EnrichAsync(
            gameFolder,
            configuration,
            paths);

        Assert.Single(requests);
        Assert.Equal("store.steampowered.com", requests[0].Host);
    }

    [Fact]
    public async Task EnrichAsync_UsesSteamGridDbForArtworkMissingFromSteam()
    {
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.ToString());

            if (uri.Host == "store.steampowered.com")
            {
                return JsonResponse(
                    """{"12170":{"success":true,"data":{}}}""");
            }

            if (uri.Host == "cdn.cloudflare.steamstatic.com")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (uri.Host == "www.steamgriddb.com")
            {
                Assert.Equal(
                    "Bearer test-key",
                    request.Headers.Authorization?.ToString());
            }

            if (uri.AbsolutePath.EndsWith("/games/steam/12170"))
                return JsonResponse("""{"success":true,"data":{"id":42}}""");

            if (uri.AbsolutePath.Contains("/grids/game/42"))
            {
                return JsonResponse(
                    """{"success":true,"data":[{"url":"https://images.test/cover.jpg"}]}""");
            }

            if (uri.Host == "images.test")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3, 4])
                    {
                        Headers = { ContentType = new("image/jpeg") }
                    }
                };
            }

            return JsonResponse("""{"success":true,"data":[]}""");
        });

        var paths = CreatePaths();
        await File.WriteAllTextAsync(
            Path.Combine(paths.Config, "metadata.json"),
            """{"steamGridDbApiKey":"test-key"}""");

        var gameFolder = CreateGameFolder(paths, "SteamGridDB Fallback");
        var configuration = CreateConfiguration("12170");

        var result = await CreateService(handler).EnrichAsync(
            gameFolder,
            configuration,
            paths);

        Assert.True(
            File.Exists(
                Path.Combine(gameFolder, "Artwork", "Cover.jpg")),
            string.Join(" | ", result.Warnings.Concat(requests)));
    }

    private PortablePaths CreatePaths()
    {
        var paths = PortablePaths.FromRoot(_root);
        paths.EnsureWritableFolders();
        Directory.CreateDirectory(paths.Games);
        return paths;
    }

    private static string CreateGameFolder(
        PortablePaths paths,
        string name)
    {
        var folder = Path.Combine(paths.Games, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static GameConfiguration CreateConfiguration(string steamId) =>
        new()
        {
            Game = new GameInformation { Name = "Test" },
            Artwork = new ArtworkConfiguration
            {
                SteamMetadataId = steamId,
                DownloadMissingArtwork = true
            }
        };

    private static SteamMetadataService CreateService(
        HttpMessageHandler handler) =>
        new(new HttpClient(handler), new GamePathResolver());

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
