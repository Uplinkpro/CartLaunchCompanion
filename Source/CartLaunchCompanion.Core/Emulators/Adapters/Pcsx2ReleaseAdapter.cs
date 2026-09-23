using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators.Adapters;

/// <summary>Discovers official PCSX2 Stable and Nightly Windows/Linux x64 packages.</summary>
public sealed class Pcsx2ReleaseAdapter : IEmulatorReleaseAdapter, IDisposable
{
    public const string Repository = "PCSX2/pcsx2";
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
    public string EmulatorId => "pcsx2";

    public Pcsx2ReleaseAdapter()
    {
        _client = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _ownsClient = true;
    }
    internal Pcsx2ReleaseAdapter(HttpClient client) => _client = client;

    public async Task<EmulatorRelease?> FindLatestAsync(EmulatorReleaseChannel channel, PlatformKind platform,
        Architecture architecture, CancellationToken cancellationToken = default)
    {
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux) || architecture != Architecture.X64)
            throw new NotSupportedException("This PCSX2 adapter supports Windows and Linux x64 only.");
        ValidateChannel(channel);
        var endpoint = channel.Id == StableChannelId
            ? new Uri($"https://api.github.com/repos/{Repository}/releases/latest")
            : new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=20");
        var bytes = await GetAsync(endpoint, cancellationToken);
        GitHubRelease? release;
        if (channel.Id == StableChannelId)
            release = JsonSerializer.Deserialize<GitHubRelease>(bytes, JsonOptions);
        else
            release = JsonSerializer.Deserialize<GitHubRelease[]>(bytes, JsonOptions)?
                .FirstOrDefault(item => !item.Draft && item.Prerelease);
        if (release is null || release.Draft) return null;
        if (release.Prerelease != (channel.Id == NightlyChannelId)) return null;
        if (release.Id <= 0 || release.PublishedAt is null || release.Assets is null ||
            !release.TagName.StartsWith('v') || !Version.TryParse(release.TagName[1..], out _))
            throw new InvalidDataException("PCSX2 release metadata is incomplete.");
        var releasePage = $"{RepositoryUrl}/releases/tag/{release.TagName}";
        if (release.HtmlUrl != releasePage) throw new InvalidDataException("The PCSX2 release page is invalid.");
        var assetName = platform == PlatformKind.Windows
            ? $"pcsx2-{release.TagName}-windows-x64-Qt.7z"
            : $"pcsx2-{release.TagName}-linux-appimage-x64-Qt.AppImage";
        var matches = release.Assets.Where(item => item.Name == assetName).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) throw new InvalidDataException("The PCSX2 release contains ambiguous packages.");
        var asset = matches[0];
        var downloadUrl = $"{RepositoryUrl}/releases/download/{release.TagName}/{assetName}";
        if (asset.Id <= 0 || asset.State != "uploaded" || asset.Size is <= 0 or > MaximumPackageBytes ||
            asset.UpdatedAt == default || asset.BrowserDownloadUrl != downloadUrl)
            throw new InvalidDataException("The PCSX2 package metadata is invalid.");
        var sha256 = ParseDigest(asset.Digest);
        var revisionData = $"{release.Id}\n{asset.Id}\n{asset.UpdatedAt:O}\n{asset.Size}\n{sha256 ?? "no-digest"}";
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionData))).ToLowerInvariant();
        return new()
        {
            EmulatorId = EmulatorId, ChannelId = channel.Id, Platform = platform, Architecture = architecture,
            Version = release.TagName, RevisionId = "pcsx2:" + revision, PublishedAt = release.PublishedAt.Value,
            IsPrerelease = release.Prerelease, ReleasePage = new(releasePage), AssetName = assetName,
            DownloadUrl = new(downloadUrl), SizeBytes = asset.Size,
            PackageFormat = platform == PlatformKind.Windows ? EmulatorPackageFormat.SevenZip : EmulatorPackageFormat.AppImage,
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
            throw new HttpRequestException($"PCSX2 release discovery returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.RequestMessage?.RequestUri != endpoint) throw new InvalidDataException("The PCSX2 release endpoint redirected.");
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes) throw new InvalidDataException("PCSX2 metadata is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumMetadataBytes) throw new InvalidDataException("PCSX2 metadata is too large.");
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
            _ => throw new InvalidDataException("This adapter requires the official PCSX2 Stable or Nightly channel.")
        };
        if (channel.SupportedPlatforms is null || !channel.SupportedPlatforms.Contains(PlatformKind.Windows) ||
            !channel.SupportedPlatforms.Contains(PlatformKind.Linux) ||
            channel.Source is not { Kind: EmulatorReleaseSourceKind.GitHubReleases, Url: RepositoryUrl,
                Tag: null, TagPrefix: "v" } source || source.Prereleases != policy)
            throw new InvalidDataException("This adapter requires the official PCSX2 Stable or Nightly channel.");
    }

    private static string? ParseDigest(string? digest)
    {
        if (digest is null) return null;
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException("The PCSX2 digest is invalid.");
        return digest[7..].ToLowerInvariant();
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
    private sealed record GitHubRelease(long Id, string TagName, bool Draft, bool Prerelease,
        DateTimeOffset? PublishedAt, string HtmlUrl, GitHubAsset[] Assets);
    private sealed record GitHubAsset(long Id, string Name, string State, long Size,
        DateTimeOffset UpdatedAt, string BrowserDownloadUrl, string? Digest);
}
