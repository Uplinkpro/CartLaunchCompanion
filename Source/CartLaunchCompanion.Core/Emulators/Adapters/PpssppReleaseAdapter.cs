using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators.Adapters;

/// <summary>
/// Discovers the latest official stable PPSSPP standalone x64 packages.
/// It does not download packages, select a different channel, or update the registry.
/// </summary>
public sealed class PpssppReleaseAdapter : IEmulatorReleaseAdapter, IDisposable
{
    public const string Repository = "hrydgard/ppsspp";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public const string ChannelId = "stable";
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private static readonly Uri Endpoint = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        MaxDepth = 32
    };
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public string EmulatorId => "ppsspp";

    public PpssppReleaseAdapter()
    {
        _httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _ownsClient = true;
    }

    // Tests inject a no-network handler. Production uses the bounded, non-redirecting client above.
    internal PpssppReleaseAdapter(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<EmulatorRelease?> FindLatestAsync(
        EmulatorReleaseChannel channel,
        PlatformKind platform,
        Architecture architecture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux) || architecture != Architecture.X64)
            throw new NotSupportedException("This PPSSPP adapter supports Windows/Linux x64 only.");
        ValidateChannel(channel, platform);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        cancellationToken = deadline.Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CartLaunchCompanion.EmulatorCompanion", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        // A removed release or unavailable repository is an error, not evidence of "no updates".
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"PPSSPP release discovery returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.RequestMessage?.RequestUri is { } finalUri && finalUri != Endpoint)
            throw new InvalidDataException("The PPSSPP release endpoint unexpectedly redirected.");
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes)
            throw new InvalidDataException("PPSSPP release metadata exceeds the size limit.");

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumMetadataBytes)
                throw new InvalidDataException("PPSSPP release metadata exceeds the size limit.");
            buffer.Write(chunk, 0, read);
        }

        var release = JsonSerializer.Deserialize<GitHubRelease>(buffer.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("PPSSPP release metadata cannot be null.");
        if (release.Draft || release.Prerelease)
            return null;
        var releaseTag = release.TagName;
        if (releaseTag.Length < 2 || releaseTag[0] != 'v' ||
            releaseTag[1..].Any(c => !char.IsAsciiDigit(c) && c != '.') ||
            !Version.TryParse(releaseTag[1..], out _))
            throw new InvalidDataException("The PPSSPP release tag is not a supported stable version.");
        if (release.Id <= 0 || release.PublishedAt is null || release.Assets is null)
            throw new InvalidDataException("PPSSPP release metadata is incomplete.");
        var releasePage = $"{RepositoryUrl}/releases/tag/{releaseTag}";
        if (release.HtmlUrl != releasePage)
            throw new InvalidDataException("The release page does not belong to the selected PPSSPP project.");

        var assetName = platform == PlatformKind.Windows ? $"PPSSPP-{releaseTag}-Windows-x64.zip" : $"PPSSPP-{releaseTag}-anylinux-x86_64.AppImage";
        if (release.Assets.Any(asset => asset is null))
            throw new InvalidDataException("Release asset entries cannot be null.");
        var matches = release.Assets.Where(asset => asset.Name == assetName).ToArray();
        if (matches.Length == 0)
            return null;
        if (matches.Length != 1)
            throw new InvalidDataException("The release contains ambiguous PPSSPP packages.");
        var asset = matches[0];
        if (asset.State != "uploaded" || asset.Id <= 0 || asset.Size <= 0 || asset.Size > MaximumPackageBytes ||
            asset.UpdatedAt == default)
            throw new InvalidDataException("The PPSSPP package is incomplete or invalid.");
        var downloadUrl = $"{RepositoryUrl}/releases/download/{releaseTag}/{assetName}";
        if (asset.BrowserDownloadUrl != downloadUrl)
            throw new InvalidDataException("The package URL does not match the selected PPSSPP release.");
        string? sha256 = null;
        if (asset.Digest is { } digest)
        {
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("The package digest is not a valid SHA-256 digest.");
            sha256 = digest[7..].ToLowerInvariant();
        }

        // Content digest plus upstream asset revision differentiates replacements under a moving tag.
        var revisionData = string.Join("\n", Repository, releaseTag, release.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            asset.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), asset.UpdatedAt.ToUniversalTime().ToString("O"),
            asset.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), sha256 ?? "no-digest");
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionData))).ToLowerInvariant();
        return new EmulatorRelease
        {
            EmulatorId = EmulatorId, ChannelId = channel.Id, Platform = platform, Architecture = architecture,
            Version = release.TagName, RevisionId = "ppsspp:" + revision, PublishedAt = release.PublishedAt.Value,
            IsPrerelease = false, ReleasePage = new Uri(releasePage), AssetName = assetName,
            DownloadUrl = new Uri(downloadUrl), SizeBytes = asset.Size, Sha256 = sha256,
            PackageFormat = platform == PlatformKind.Windows ? EmulatorPackageFormat.Zip : EmulatorPackageFormat.AppImage
        };
    }

    private static void ValidateChannel(EmulatorReleaseChannel channel, PlatformKind platform)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.Id != ChannelId || !channel.IsStable || channel.SupportedPlatforms is null ||
            !channel.SupportedPlatforms.Contains(platform) || channel.Source is not
            {
                Kind: EmulatorReleaseSourceKind.GitHubReleases,
                Url: RepositoryUrl,
                Tag: null,
                TagPrefix: "v",
                Prereleases: EmulatorPrereleasePolicy.Exclude
            })
            throw new InvalidDataException("This adapter requires the official PPSSPP stable channel.");
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }

    private sealed record GitHubRelease
    {
        public required long Id { get; init; }
        public required string TagName { get; init; }
        public required bool Draft { get; init; }
        public required bool Prerelease { get; init; }
        public required DateTimeOffset? PublishedAt { get; init; }
        public required string HtmlUrl { get; init; }
        public required GitHubAsset[] Assets { get; init; }
    }

    private sealed record GitHubAsset
    {
        public required long Id { get; init; }
        public required string Name { get; init; }
        public required string State { get; init; }
        public required long Size { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
        public required string BrowserDownloadUrl { get; init; }
        public string? Digest { get; init; }
    }
}
