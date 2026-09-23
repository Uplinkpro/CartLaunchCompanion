using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators.Adapters;

/// <summary>Discovers official DuckStation Windows and Linux x64 portable builds for its stable and preview channels.</summary>
public sealed class DuckStationReleaseAdapter : IEmulatorReleaseAdapter, IDisposable
{
    public const string Repository = "stenzek/duckstation";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public const string StableChannelId = "stable";
    public const string PreviewChannelId = "preview";
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        MaxDepth = 32
    };
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public string EmulatorId => "duckstation";

    public DuckStationReleaseAdapter()
    {
        _httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _ownsClient = true;
    }

    internal DuckStationReleaseAdapter(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<EmulatorRelease?> FindLatestAsync(EmulatorReleaseChannel channel, PlatformKind platform,
        Architecture architecture, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux) || architecture != Architecture.X64)
            throw new NotSupportedException("This DuckStation adapter supports Windows and Linux x64 only.");
        var assetName = platform == PlatformKind.Windows
            ? "duckstation-windows-x64-release.zip" : "DuckStation-x64.AppImage";
        var tag = ValidateChannel(channel);
        var endpoint = new Uri($"https://api.github.com/repos/{Repository}/releases/tags/{tag}");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CartLaunchCompanion.EmulatorCompanion", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"DuckStation release discovery returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.RequestMessage?.RequestUri is { } finalUri && finalUri != endpoint)
            throw new InvalidDataException("The DuckStation release endpoint unexpectedly redirected.");
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes)
            throw new InvalidDataException("DuckStation release metadata exceeds the size limit.");

        await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, deadline.Token)) > 0)
        {
            if (buffer.Length + read > MaximumMetadataBytes)
                throw new InvalidDataException("DuckStation release metadata exceeds the size limit.");
            buffer.Write(chunk, 0, read);
        }

        var release = JsonSerializer.Deserialize<GitHubRelease>(buffer.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("DuckStation release metadata cannot be null.");
        if (release.Draft) return null;
        var expectedPrerelease = channel.Id == PreviewChannelId;
        if (release.Prerelease != expectedPrerelease) return null;
        if (release.Id <= 0 || release.TagName != tag || release.PublishedAt is null || release.Assets is null)
            throw new InvalidDataException("DuckStation release metadata is incomplete or belongs to another channel.");
        var releasePage = $"{RepositoryUrl}/releases/tag/{tag}";
        if (release.HtmlUrl != releasePage)
            throw new InvalidDataException("The release page does not belong to the selected DuckStation project.");
        if (release.Assets.Any(asset => asset is null))
            throw new InvalidDataException("Release asset entries cannot be null.");
        var matches = release.Assets.Where(asset => asset.Name == assetName).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1)
            throw new InvalidDataException("The release contains ambiguous DuckStation packages.");
        var asset = matches[0];
        if (asset.State != "uploaded" || asset.Id <= 0 || asset.Size <= 0 || asset.Size > MaximumPackageBytes ||
            asset.UpdatedAt == default)
            throw new InvalidDataException("The DuckStation package is incomplete or invalid.");
        var downloadUrl = $"{RepositoryUrl}/releases/download/{tag}/{assetName}";
        if (asset.BrowserDownloadUrl != downloadUrl)
            throw new InvalidDataException("The package URL does not match the selected DuckStation channel.");
        string? sha256 = null;
        if (asset.Digest is { } digest)
        {
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 ||
                !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("The package digest is not a valid SHA-256 digest.");
            sha256 = digest[7..].ToLowerInvariant();
        }

        var revisionData = string.Join("\n", Repository, tag,
            release.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            asset.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), asset.UpdatedAt.ToUniversalTime().ToString("O"),
            asset.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), sha256 ?? "no-digest");
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionData))).ToLowerInvariant();
        return new EmulatorRelease
        {
            EmulatorId = EmulatorId, ChannelId = channel.Id, Platform = platform, Architecture = architecture,
            Version = $"{channel.Id}-{asset.UpdatedAt.UtcDateTime:yyyy.MM.dd.HHmmss}",
            RevisionId = "duckstation:" + revision, PublishedAt = release.PublishedAt.Value,
            IsPrerelease = release.Prerelease, ReleasePage = new(releasePage), AssetName = assetName,
            DownloadUrl = new(downloadUrl), SizeBytes = asset.Size,
            PackageFormat = platform == PlatformKind.Windows ? EmulatorPackageFormat.Zip : EmulatorPackageFormat.AppImage,
            Sha256 = sha256
        };
    }

    private static string ValidateChannel(EmulatorReleaseChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var tag = channel.Id switch
        {
            StableChannelId when channel.IsStable => "latest",
            PreviewChannelId when !channel.IsStable => "preview",
            _ => throw new InvalidDataException("This adapter requires an official DuckStation stable or preview channel.")
        };
        var prereleases = channel.Id == PreviewChannelId ? EmulatorPrereleasePolicy.Only : EmulatorPrereleasePolicy.Exclude;
        if (channel.SupportedPlatforms is null || !channel.SupportedPlatforms.Contains(PlatformKind.Windows) ||
            !channel.SupportedPlatforms.Contains(PlatformKind.Linux) ||
            channel.Source is not { Kind: EmulatorReleaseSourceKind.GitHubReleases, Url: RepositoryUrl,
                TagPrefix: null } source || source.Tag != tag || source.Prereleases != prereleases)
            throw new InvalidDataException("This adapter requires an official DuckStation stable or preview channel.");
        return tag;
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
