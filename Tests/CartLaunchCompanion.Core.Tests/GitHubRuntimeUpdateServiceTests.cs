using System.Net;
using System.Text;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GitHubRuntimeUpdateServiceTests
{
    [Fact]
    public async Task CheckAsyncReturnsMatchingNewerPlatformAssets()
    {
        using var client = new HttpClient(new StaticHandler(ReleaseJson("v2.3.0", 42_000_000)));
        var service = new GitHubRuntimeUpdateService(client);

        var update = await service.CheckAsync(new Version(2, 2, 0), "Windows-x64");

        Assert.NotNull(update);
        Assert.Equal("2.3.0", update.Version);
        Assert.Equal(42_000_000, update.PayloadBytes);
        Assert.EndsWith("update-win-x64.json", update.ManifestUri.AbsoluteUri);
        Assert.EndsWith("runtime-win-x64.zip", update.PayloadUri.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAsyncIgnoresOlderRelease()
    {
        using var client = new HttpClient(new StaticHandler(ReleaseJson("v1.0.0", 42_000_000)));
        var service = new GitHubRuntimeUpdateService(client);

        var update = await service.CheckAsync(new Version(2, 2, 0), "Linux-x64");

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckAsyncRejectsOversizedPayload()
    {
        using var client = new HttpClient(new StaticHandler(ReleaseJson("v2.3.0", 2_000_000_000)));
        var service = new GitHubRuntimeUpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.CheckAsync(new Version(2, 2, 0), "Windows-x64"));
    }

    [Fact]
    public async Task CheckAsyncRejectsIncompleteRelease()
    {
        const string json = """
            {"tag_name":"v2.3.0","html_url":"https://github.com/Uplinkpro/CartLaunchCompanion/releases/tag/v2.3.0","assets":[]}
            """;
        using var client = new HttpClient(new StaticHandler(json));
        var service = new GitHubRuntimeUpdateService(client);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.CheckAsync(new Version(2, 2, 0), "Windows-x64"));
    }

    [Theory]
    [InlineData("http://github.com/Uplinkpro/release.zip")]
    [InlineData("https://github.com.evil.example/Uplinkpro/release.zip")]
    [InlineData("https://evil.github.com/Uplinkpro/release.zip")]
    [InlineData("https://github.com:8443/Uplinkpro/release.zip")]
    [InlineData("https://user@github.com/Uplinkpro/release.zip")]
    public void OriginPolicyRejectsUnsafeUris(string value)
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateDownloadOriginPolicy.Validate(new Uri(value)));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/Uplinkpro/CartLaunchCompanion/releases/latest")]
    [InlineData("https://github.com/Uplinkpro/CartLaunchCompanion/releases/download/v2.3.0/update.json")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/file")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/file")]
    public void OriginPolicyAcceptsExactApprovedHosts(string value)
    {
        UpdateDownloadOriginPolicy.Validate(new Uri(value));
    }

    [Fact]
    public async Task CheckAsyncRejectsRedirectToUnapprovedHost()
    {
        using var client = new HttpClient(new RedirectHandler(
            new Uri("https://downloads.evil.example/latest")));
        var service = new GitHubRuntimeUpdateService(client);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.CheckAsync(new Version(2, 2, 0), "Windows-x64"));

        Assert.Contains("origin is not approved", error.Message);
    }

    [Fact]
    public async Task CheckAsyncFollowsApprovedRedirect()
    {
        using var client = new HttpClient(new RedirectHandler(
            new Uri("https://github.com/Uplinkpro/CartLaunchCompanion/releases/latest"),
            ReleaseJson("v2.3.0", 42_000_000)));
        var service = new GitHubRuntimeUpdateService(client);

        var update = await service.CheckAsync(new Version(2, 2, 0), "Windows-x64");

        Assert.NotNull(update);
        Assert.Equal("2.3.0", update.Version);
    }

    private static string ReleaseJson(string tag, long size) => $$"""
        {
          "tag_name":"{{tag}}",
          "html_url":"https://github.com/Uplinkpro/CartLaunchCompanion/releases/tag/{{tag}}",
          "assets":[
            {"name":"update-win-x64.json","size":100,"browser_download_url":"https://github.com/Uplinkpro/CartLaunchCompanion/releases/download/{{tag}}/update-win-x64.json"},
            {"name":"runtime-win-x64.zip","size":{{size}},"browser_download_url":"https://github.com/Uplinkpro/CartLaunchCompanion/releases/download/{{tag}}/runtime-win-x64.zip"},
            {"name":"update-linux-x64.json","size":100,"browser_download_url":"https://github.com/Uplinkpro/CartLaunchCompanion/releases/download/{{tag}}/update-linux-x64.json"},
            {"name":"runtime-linux-x64.tar.gz","size":{{size}},"browser_download_url":"https://github.com/Uplinkpro/CartLaunchCompanion/releases/download/{{tag}}/runtime-linux-x64.tar.gz"}
          ]
        }
        """;

    private sealed class StaticHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class RedirectHandler(Uri location, string? finalJson = null) : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests++;
            if (_requests == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = location;
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(finalJson ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
