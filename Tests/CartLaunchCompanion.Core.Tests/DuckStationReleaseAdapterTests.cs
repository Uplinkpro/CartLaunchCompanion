using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Emulators.Adapters;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class DuckStationReleaseAdapterTests
{
    private static EmulatorReleaseChannel Channel(string id = "stable") => new()
    {
        Id = id, DisplayName = id == "stable" ? "Stable" : "Preview", IsStable = id == "stable",
        SupportedPlatforms = [PlatformKind.Windows, PlatformKind.Linux],
        Source = new()
        {
            Kind = EmulatorReleaseSourceKind.GitHubReleases,
            Url = DuckStationReleaseAdapter.RepositoryUrl,
            Tag = id == "stable" ? "latest" : "preview",
            Prereleases = id == "stable" ? EmulatorPrereleasePolicy.Exclude : EmulatorPrereleasePolicy.Only
        }
    };

    private static JsonObject Release(string tag = "latest", bool prerelease = false) => new()
    {
        ["id"] = 501, ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = prerelease,
        ["published_at"] = "2026-09-12T12:20:15Z",
        ["html_url"] = $"{DuckStationReleaseAdapter.RepositoryUrl}/releases/tag/{tag}",
        ["assets"] = new JsonArray(
            new JsonObject
            {
                ["id"] = 801, ["name"] = "duckstation-windows-x64-installer.exe", ["state"] = "uploaded",
                ["size"] = 40_000_000, ["updated_at"] = "2026-09-12T12:20:16Z",
                ["browser_download_url"] = $"{DuckStationReleaseAdapter.RepositoryUrl}/releases/download/{tag}/duckstation-windows-x64-installer.exe",
                ["digest"] = "sha256:" + new string('b', 64)
            },
            new JsonObject
            {
                ["id"] = 802, ["name"] = "duckstation-windows-x64-release.zip", ["state"] = "uploaded",
                ["size"] = 69_300_000, ["updated_at"] = "2026-09-12T12:20:20Z",
                ["browser_download_url"] = $"{DuckStationReleaseAdapter.RepositoryUrl}/releases/download/{tag}/duckstation-windows-x64-release.zip",
                ["digest"] = "sha256:" + new string('a', 64)
            },
            new JsonObject
            {
                ["id"] = 803, ["name"] = "DuckStation-x64.AppImage", ["state"] = "uploaded",
                ["size"] = 75_000_000, ["updated_at"] = "2026-09-12T12:20:21Z",
                ["browser_download_url"] = $"{DuckStationReleaseAdapter.RepositoryUrl}/releases/download/{tag}/DuckStation-x64.AppImage",
                ["digest"] = "sha256:" + new string('c', 64)
            })
    };

    [Theory]
    [InlineData("stable", "latest", false)]
    [InlineData("preview", "preview", true)]
    public async Task DiscoversExactOfficialPortableBuild(string channelId, string tag, bool prerelease)
    {
        using var handler = new Handler(Release(tag, prerelease).ToJsonString());
        using var adapter = new DuckStationReleaseAdapter(new HttpClient(handler));
        var result = await adapter.FindLatestAsync(Channel(channelId), PlatformKind.Windows, Architecture.X64);
        Assert.NotNull(result);
        Assert.Equal("duckstation-windows-x64-release.zip", result.AssetName);
        Assert.Equal(channelId + "-2026.09.12.122020", result.Version);
        Assert.Equal(prerelease, result.IsPrerelease);
        Assert.Equal(new string('a', 64), result.Sha256);
        Assert.Equal($"https://api.github.com/repos/stenzek/duckstation/releases/tags/{tag}", handler.RequestUrl);
    }

    [Theory]
    [InlineData("stable", "latest", false)]
    [InlineData("preview", "preview", true)]
    public async Task DiscoversExactOfficialLinuxAppImage(string channelId, string tag, bool prerelease)
    {
        using var handler = new Handler(Release(tag, prerelease).ToJsonString());
        using var adapter = new DuckStationReleaseAdapter(new HttpClient(handler));
        var result = await adapter.FindLatestAsync(Channel(channelId), PlatformKind.Linux, Architecture.X64);
        Assert.NotNull(result);
        Assert.Equal("DuckStation-x64.AppImage", result.AssetName);
        Assert.Equal(channelId + "-2026.09.12.122021", result.Version);
        Assert.Equal(EmulatorPackageFormat.AppImage, result.PackageFormat);
        Assert.Equal(new string('c', 64), result.Sha256);
    }

    [Fact]
    public async Task MovingAssetRevisionChangesIdentity()
    {
        var data = Release();
        var first = await Discover(data);
        data["assets"]![1]!["digest"] = "sha256:" + new string('c', 64);
        Assert.NotEqual(first!.RevisionId, (await Discover(data))!.RevisionId);
    }

    [Fact]
    public async Task MissingPortableBuildReturnsNull()
    {
        var data = Release();
        data["assets"]!.AsArray().RemoveAt(1);
        Assert.Null(await Discover(data));
    }

    [Theory]
    [InlineData(PlatformKind.Linux, Architecture.Arm64)]
    [InlineData(PlatformKind.Windows, Architecture.Arm64)]
    public async Task UnsupportedTargetsMakeNoRequest(PlatformKind platform, Architecture architecture)
    {
        using var handler = new Handler("{}");
        using var adapter = new DuckStationReleaseAdapter(new HttpClient(handler));
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.FindLatestAsync(Channel(), platform, architecture));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ChannelMetadataAndReleaseKindMustMatch()
    {
        using var adapter = new DuckStationReleaseAdapter(new HttpClient(new Handler(Release().ToJsonString())));
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.FindLatestAsync(
            Channel() with { Source = Channel().Source! with { Tag = "preview" } }, PlatformKind.Windows, Architecture.X64));
        Assert.Null(await adapter.FindLatestAsync(Channel("preview"), PlatformKind.Windows, Architecture.X64));
    }

    [Fact]
    public async Task InvalidDigestAndPackageUrlAreRejected()
    {
        var digest = Release();
        digest["assets"]![1]!["digest"] = "sha256:invalid";
        await Assert.ThrowsAsync<InvalidDataException>(() => Discover(digest));
        var url = Release();
        url["assets"]![1]!["browser_download_url"] = "https://example.com/package.zip";
        await Assert.ThrowsAsync<InvalidDataException>(() => Discover(url));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task HttpFailuresRemainErrors(HttpStatusCode status)
    {
        using var adapter = new DuckStationReleaseAdapter(new HttpClient(new Handler("{}", status)));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            adapter.FindLatestAsync(Channel(), PlatformKind.Windows, Architecture.X64));
        Assert.Equal(status, error.StatusCode);
    }

    private static async Task<EmulatorRelease?> Discover(JsonObject data)
    {
        using var adapter = new DuckStationReleaseAdapter(new HttpClient(new Handler(data.ToJsonString())));
        return await adapter.FindLatestAsync(Channel(), PlatformKind.Windows, Architecture.X64);
    }

    private sealed class Handler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? RequestUrl { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
