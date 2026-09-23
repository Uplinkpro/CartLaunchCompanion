using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Emulators.Adapters;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PpssppReleaseAdapterTests
{
    private static EmulatorReleaseChannel Channel => new()
    {
        Id = "stable", DisplayName = "Stable", IsStable = true,
        SupportedPlatforms = [PlatformKind.Windows, PlatformKind.Linux],
        Source = new()
        {
            Kind = EmulatorReleaseSourceKind.GitHubReleases,
            Url = PpssppReleaseAdapter.RepositoryUrl,
            TagPrefix = "v",
            Prereleases = EmulatorPrereleasePolicy.Exclude
        }
    };

    private static JsonObject Asset(string name, int id) => new()
    {
        ["id"] = id, ["name"] = name, ["state"] = "uploaded", ["size"] = 1234,
        ["updated_at"] = "2026-09-04T23:30:46Z",
        ["browser_download_url"] = $"{PpssppReleaseAdapter.RepositoryUrl}/releases/download/v1.20.4/{name}",
        ["digest"] = "sha256:" + new string('a', 64)
    };

    private static JsonObject Release() => new()
    {
        ["id"] = 383063655, ["tag_name"] = "v1.20.4", ["draft"] = false,
        ["prerelease"] = false, ["published_at"] = "2026-09-04T23:30:47Z",
        ["html_url"] = PpssppReleaseAdapter.RepositoryUrl + "/releases/tag/v1.20.4",
        ["assets"] = new JsonArray(
            Asset("PPSSPP-v1.20.4-Windows-ARM64.zip", 1),
            Asset("app-mainline-release.apk", 2),
            Asset("PPSSPP-v1.20.4-Windows-x64.zip", 3),
            Asset("PPSSPP-v1.20.4-anylinux-x86_64.AppImage", 4),
            Asset("PPSSPP-v1.20.4-anylinux-x86_64.AppImage.zsync", 5),
            Asset("ppsspp-1.20.4.tar.xz", 6),
            Asset("PPSSPP-v1.20.4-anylinux-aarch64.AppImage", 7))
    };

    [Theory]
    [InlineData(PlatformKind.Windows, "PPSSPP-v1.20.4-Windows-x64.zip", EmulatorPackageFormat.Zip)]
    [InlineData(PlatformKind.Linux, "PPSSPP-v1.20.4-anylinux-x86_64.AppImage", EmulatorPackageFormat.AppImage)]
    public async Task SelectsExactStandaloneX64Package(PlatformKind platform, string name, EmulatorPackageFormat format)
    {
        using var handler = new Handler(Release().ToJsonString());
        using var client = new HttpClient(handler);
        using var adapter = new PpssppReleaseAdapter(client);
        var result = await adapter.FindLatestAsync(Channel, platform, Architecture.X64);
        Assert.NotNull(result);
        Assert.Equal(name, result.AssetName);
        Assert.Equal(format, result.PackageFormat);
        Assert.False(result.IsPrerelease);
        Assert.Equal("v1.20.4", result.Version);
        Assert.Equal(new string('a', 64), result.Sha256);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("https://api.github.com/repos/hrydgard/ppsspp/releases/latest", handler.RequestUrl);
        Assert.Contains("CartLaunchCompanion.EmulatorCompanion", handler.UserAgent);
    }

    [Fact]
    public async Task SameRevisionIsStableButAssetReplacementChangesIdentity()
    {
        var data = Release();
        var first = await Discover(data);
        Assert.Equal(first!.RevisionId, (await Discover(data))!.RevisionId);
        data["assets"]![2]!["updated_at"] = "2026-09-05T23:30:46Z";
        Assert.NotEqual(first.RevisionId, (await Discover(data))!.RevisionId);
        data["assets"]![2]!["updated_at"] = "2026-09-04T23:30:46Z";
        data["assets"]![2]!["digest"] = "sha256:" + new string('b', 64);
        Assert.NotEqual(first.RevisionId, (await Discover(data))!.RevisionId);
    }

    [Theory]
    [InlineData("draft", true)]
    [InlineData("prerelease", true)]
    public async Task ExcludesDraftsAndReleasesOutsideSelectedChannel(string property, bool value)
    {
        var data = Release();
        data[property] = value;
        Assert.Null(await Discover(data));
    }

    [Fact]
    public async Task NoMatchingStandalonePackageReturnsNull()
    {
        var data = Release();
        data["assets"]!.AsArray().RemoveAt(2);
        Assert.Null(await Discover(data));
    }

    [Fact]
    public async Task DuplicateStandaloneAssetsAreRejected()
    {
        var data = Release();
        data["assets"]!.AsArray().Add(Asset("PPSSPP-v1.20.4-Windows-x64.zip", 10));
        await Assert.ThrowsAsync<InvalidDataException>(() => Discover(data));
    }

    [Theory]
    [InlineData("tag_name", "different-tag")]
    [InlineData("html_url", "https://example.com/release")]
    public async Task RejectsReleaseSourceMismatch(string property, string value)
    {
        var data = Release();
        data[property] = value;
        await Assert.ThrowsAsync<InvalidDataException>(() => Discover(data));
    }

    [Theory]
    [InlineData("browser_download_url", "https://example.com/PPSSPP-v1.20.4-Windows-x64.zip")]
    [InlineData("browser_download_url", "https://github.com/other/project/releases/download/v1.20.4/PPSSPP-v1.20.4-Windows-x64.zip")]
    [InlineData("browser_download_url", "http://github.com/hrydgard/ppsspp/releases/download/v1.20.4/PPSSPP-v1.20.4-Windows-x64.zip")]
    [InlineData("state", "new")]
    [InlineData("digest", "sha256:invalid")]
    public async Task RejectsInvalidPackageMetadata(string property, string value)
    {
        var data = Release();
        data["assets"]![2]![property] = value;
        await Assert.ThrowsAsync<InvalidDataException>(() => Discover(data));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(2147483649L)]
    public async Task RejectsInvalidPackageSize(long size)
    {
        var data = Release();
        data["assets"]![2]!["size"] = size;
        await Assert.ThrowsAsync<InvalidDataException>(() => Discover(data));
    }

    [Fact]
    public async Task MissingDigestIsExplicitRatherThanInvented()
    {
        var data = Release();
        data["assets"]![2]!.AsObject().Remove("digest");
        Assert.Null((await Discover(data))!.Sha256);
    }

    [Theory]
    [InlineData(PlatformKind.Unsupported, Architecture.X64)]
    [InlineData(PlatformKind.Windows, Architecture.Arm64)]
    [InlineData(PlatformKind.Linux, Architecture.X86)]
    public async Task UnsupportedTargetsMakeNoRequest(PlatformKind platform, Architecture architecture)
    {
        using var handler = new Handler("[]");
        using var client = new HttpClient(handler);
        using var adapter = new PpssppReleaseAdapter(client);
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.FindLatestAsync(Channel, platform, architecture));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DifferentChannelOrRepositoryMakesNoRequest()
    {
        using var handler = new Handler("[]");
        using var client = new HttpClient(handler);
        using var adapter = new PpssppReleaseAdapter(client);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.FindLatestAsync(Channel with { Id = "preview" }, PlatformKind.Windows, Architecture.X64));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.FindLatestAsync(Channel with { Source = Channel.Source! with { Url = "https://github.com/other/project" } },
                PlatformKind.Windows, Architecture.X64));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task HttpFailuresAreNotReportedAsNoUpdate(HttpStatusCode status)
    {
        using var handler = new Handler("[]", status);
        using var client = new HttpClient(handler);
        using var adapter = new PpssppReleaseAdapter(client);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            adapter.FindLatestAsync(Channel, PlatformKind.Windows, Architecture.X64));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task MalformedOrIncompleteJsonFails(string json)
    {
        using var client = new HttpClient(new Handler(json));
        using var adapter = new PpssppReleaseAdapter(client);
        await Assert.ThrowsAsync<JsonException>(() => adapter.FindLatestAsync(Channel, PlatformKind.Windows, Architecture.X64));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataLimitAppliesWithAndWithoutContentLength(bool chunked)
    {
        using var client = new HttpClient(new Handler(new string(' ', 1024 * 1024 + 1), chunked: chunked));
        using var adapter = new PpssppReleaseAdapter(client);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            adapter.FindLatestAsync(Channel, PlatformKind.Windows, Architecture.X64));
    }

    [Fact]
    public async Task CancellationPropagatesWithoutRequest()
    {
        using var handler = new Handler("[]");
        using var client = new HttpClient(handler);
        using var adapter = new PpssppReleaseAdapter(client);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.FindLatestAsync(Channel, PlatformKind.Windows, Architecture.X64, cancelled.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task FollowsNewStableVersionsWithoutHardcodingTheCurrentTag()
    {
        var data = JsonNode.Parse(Release().ToJsonString().Replace("v1.20.4", "v1.21"))!.AsObject();
        var result = await Discover(data);
        Assert.Equal("v1.21", result!.Version);
        Assert.Equal("PPSSPP-v1.21-Windows-x64.zip", result.AssetName);
    }

    [Fact]
    public async Task PackageForAnotherVersionIsNotSelected()
    {
        var data = Release();
        data["assets"]![2]!["name"] = "PPSSPP-v1.19.3-Windows-x64.zip";
        Assert.Null(await Discover(data));
    }

    private static async Task<EmulatorRelease?> Discover(JsonObject data)
    {
        using var client = new HttpClient(new Handler(data.ToJsonString()));
        using var adapter = new PpssppReleaseAdapter(client);
        return await adapter.FindLatestAsync(Channel, PlatformKind.Windows, Architecture.X64);
    }

    private sealed class Handler(string json, HttpStatusCode status = HttpStatusCode.OK, bool chunked = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? RequestUrl { get; private set; }
        public string? UserAgent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUrl = request.RequestUri!.AbsoluteUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = chunked ? new UnknownLengthContent(json) : new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class UnknownLengthContent(string text) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(text)).AsTask();
    }
}
