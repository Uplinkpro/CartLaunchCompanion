using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators.Adapters;

/// <summary>Discovers official shadPS4 Stable and Nightly Windows/Linux x64 SDL packages.</summary>
public sealed partial class ShadPs4ReleaseAdapter : IEmulatorReleaseAdapter, IDisposable
{
    public const string Repository = "shadps4-emu/shadPS4";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public const string StableChannelId = "stable";
    public const string NightlyChannelId = "nightly";
    private const int MaximumMetadataBytes = 2 * 1024 * 1024;
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        MaxDepth = 32
    };
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    public string EmulatorId => "shadps4";

    public ShadPs4ReleaseAdapter()
    {
        _client = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _ownsClient = true;
    }
    internal ShadPs4ReleaseAdapter(HttpClient client) => _client = client;

    public async Task<EmulatorRelease?> FindLatestAsync(EmulatorReleaseChannel channel, PlatformKind platform,
        Architecture architecture, CancellationToken cancellationToken = default)
    {
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux) || architecture != Architecture.X64)
            throw new NotSupportedException("This shadPS4 adapter supports Windows and Linux x64 only.");
        ValidateChannel(channel);
        var endpoint = channel.Id == StableChannelId
            ? new Uri($"https://api.github.com/repos/{Repository}/releases/latest")
            : new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=20");
        var bytes = await GetAsync(endpoint, cancellationToken);
        var release = channel.Id == StableChannelId
            ? JsonSerializer.Deserialize<GitHubRelease>(bytes, JsonOptions)
            : JsonSerializer.Deserialize<GitHubRelease[]>(bytes, JsonOptions)?.FirstOrDefault(item => !item.Draft && item.Prerelease);
        if (release is null || release.Draft) return null;
        if (release.Prerelease != (channel.Id == NightlyChannelId) || release.Id <= 0 ||
            release.PublishedAt is null || release.Assets is null)
            throw new InvalidDataException("shadPS4 release metadata is incomplete.");
        var releasePage = $"{RepositoryUrl}/releases/tag/{release.TagName}";
        if (release.HtmlUrl != releasePage) throw new InvalidDataException("The shadPS4 release page is invalid.");
        var platformName = platform == PlatformKind.Windows ? "win64" : "linux";
        var pattern = channel.Id == StableChannelId
            ? $"^shadps4-{platformName}-sdl-(?<version>[0-9]+\\.[0-9]+\\.[0-9]+)\\.zip$"
            : $"^shadps4-{platformName}-sdl-(?<version>[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}-[0-9a-f]{{7}})\\.zip$";
        var matches = release.Assets.Select(asset => (Asset: asset, Match: Regex.Match(asset.Name, pattern,
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)))
            .Where(item => item.Match.Success).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) throw new InvalidDataException("The shadPS4 release contains ambiguous packages.");
        var (asset, match) = matches[0];
        if (channel.Id == StableChannelId && release.TagName != "v." + match.Groups["version"].Value)
            throw new InvalidDataException("The shadPS4 stable package does not match its release tag.");
        if (channel.Id == NightlyChannelId && !release.TagName.StartsWith("Pre-release-shadPS4-" +
                match.Groups["version"].Value[..10] + "-", StringComparison.Ordinal))
            throw new InvalidDataException("The shadPS4 nightly package does not match its release tag.");
        var downloadUrl = $"{RepositoryUrl}/releases/download/{release.TagName}/{asset.Name}";
        if (asset.Id <= 0 || asset.State != "uploaded" || asset.Size is <= 0 or > MaximumPackageBytes ||
            asset.UpdatedAt == default || asset.BrowserDownloadUrl != downloadUrl)
            throw new InvalidDataException("The shadPS4 package metadata is invalid.");
        var sha256 = ParseDigest(asset.Digest);
        var revisionData = $"{release.Id}\n{asset.Id}\n{asset.UpdatedAt:O}\n{asset.Size}\n{sha256 ?? "no-digest"}";
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionData))).ToLowerInvariant();
        return new()
        {
            EmulatorId = EmulatorId, ChannelId = channel.Id, Platform = platform, Architecture = architecture,
            Version = channel.Id == StableChannelId ? "v" + match.Groups["version"].Value : release.TagName,
            RevisionId = "shadps4:" + revision, PublishedAt = release.PublishedAt.Value,
            IsPrerelease = release.Prerelease, ReleasePage = new(releasePage), AssetName = asset.Name,
            DownloadUrl = new(downloadUrl), SizeBytes = asset.Size, PackageFormat = EmulatorPackageFormat.Zip,
            Sha256 = sha256
        };
    }

    private async Task<byte[]> GetAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CartLaunchCompanion.EmulatorCompanion", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"shadPS4 release discovery returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.RequestMessage?.RequestUri != endpoint) throw new InvalidDataException("The shadPS4 release endpoint redirected.");
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes) throw new InvalidDataException("shadPS4 metadata is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumMetadataBytes) throw new InvalidDataException("shadPS4 metadata is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void ValidateChannel(EmulatorReleaseChannel channel)
    {
        var policy = channel.Id switch
        {
            StableChannelId when channel.IsStable => EmulatorPrereleasePolicy.Exclude,
            NightlyChannelId when !channel.IsStable => EmulatorPrereleasePolicy.Only,
            _ => throw new InvalidDataException("This adapter requires the official shadPS4 Stable or Nightly channel.")
        };
        if (channel.SupportedPlatforms is null || !channel.SupportedPlatforms.Contains(PlatformKind.Windows) ||
            !channel.SupportedPlatforms.Contains(PlatformKind.Linux) ||
            channel.Source is not { Kind: EmulatorReleaseSourceKind.GitHubReleases, Url: RepositoryUrl,
                Tag: null, TagPrefix: null } source || source.Prereleases != policy)
            throw new InvalidDataException("This adapter requires the official shadPS4 Stable or Nightly channel.");
    }

    private static string? ParseDigest(string? digest)
    {
        if (digest is null) return null;
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException("The shadPS4 digest is invalid.");
        return digest[7..].ToLowerInvariant();
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
    private sealed record GitHubRelease(long Id, string TagName, bool Draft, bool Prerelease,
        DateTimeOffset? PublishedAt, string HtmlUrl, GitHubAsset[] Assets);
    private sealed record GitHubAsset(long Id, string Name, string State, long Size,
        DateTimeOffset UpdatedAt, string BrowserDownloadUrl, string? Digest);
}
