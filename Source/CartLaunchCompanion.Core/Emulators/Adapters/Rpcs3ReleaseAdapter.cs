using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators.Adapters;

/// <summary>Discovers the official rolling RPCS3 Windows/Linux x64 packages from its updater service.</summary>
public sealed class Rpcs3ReleaseAdapter : IEmulatorReleaseAdapter, IDisposable
{
    public const string ProjectUrl = "https://rpcs3.net/download";
    public const string ChannelId = "rolling";
    private const int MaximumMetadataBytes = 256 * 1024;
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        MaxDepth = 16
    };
    public string EmulatorId => "rpcs3";

    public Rpcs3ReleaseAdapter()
    {
        _client = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _ownsClient = true;
    }
    internal Rpcs3ReleaseAdapter(HttpClient client) => _client = client;

    public async Task<EmulatorRelease?> FindLatestAsync(EmulatorReleaseChannel channel, PlatformKind platform,
        Architecture architecture, CancellationToken cancellationToken = default)
    {
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux) || architecture != Architecture.X64)
            throw new NotSupportedException("This RPCS3 adapter supports Windows and Linux x64 only.");
        ValidateChannel(channel);
        var os = platform == PlatformKind.Windows ? "windows" : "linux";
        var osVersion = platform == PlatformKind.Windows ? "10.0.19045" : "6.12.0";
        var endpoint = new Uri($"https://update.rpcs3.net/?api=v3&c=00000000&os_type={os}&os_arch=x64&os_version={osVersion}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.ParseAdd("RPCS3");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"RPCS3 release discovery returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.RequestMessage?.RequestUri != endpoint) throw new InvalidDataException("The RPCS3 updater endpoint redirected.");
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes) throw new InvalidDataException("RPCS3 metadata is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumMetadataBytes) throw new InvalidDataException("RPCS3 metadata is too large.");
            output.Write(buffer, 0, read);
        }
        var metadata = JsonSerializer.Deserialize<UpdateResponse>(output.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("RPCS3 update metadata is empty.");
        if (metadata.ReturnCode is not (-1 or 0) || metadata.LatestBuild is null)
            throw new InvalidDataException("RPCS3 update metadata did not provide a current build.");
        var package = platform == PlatformKind.Windows ? metadata.LatestBuild.Windows : metadata.LatestBuild.Linux;
        if (package is null) return null;
        if (!Regex.IsMatch(metadata.LatestBuild.Version, @"^0\.0\.[0-9]+-[0-9]+$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParseExact(metadata.LatestBuild.Datetime + " +00:00", "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var published) ||
            package.Size is <= 0 or > MaximumPackageBytes || package.Checksum.Length != 64 || !package.Checksum.All(Uri.IsHexDigit) ||
            !Uri.TryCreate(package.Download, UriKind.Absolute, out var download))
            throw new InvalidDataException("RPCS3 update metadata is invalid.");
        var repository = platform == PlatformKind.Windows ? "rpcs3-binaries-win" : "rpcs3-binaries-linux";
        var suffix = platform == PlatformKind.Windows ? "_win64_msvc.7z" : "_linux64.AppImage";
        var pattern = $"^https://github\\.com/RPCS3/{repository}/releases/download/build-(?<hash>[0-9a-f]{{40}})/" +
            $"rpcs3-v{Regex.Escape(metadata.LatestBuild.Version)}-(?<short>[0-9a-f]{{8}}){Regex.Escape(suffix)}$";
        var match = Regex.Match(download.AbsoluteUri, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        if (!match.Success || !match.Groups["hash"].Value.StartsWith(match.Groups["short"].Value, StringComparison.Ordinal))
            throw new InvalidDataException("The RPCS3 package URL is invalid.");
        var tag = "build-" + match.Groups["hash"].Value;
        return new()
        {
            EmulatorId = EmulatorId, ChannelId = ChannelId, Platform = platform, Architecture = architecture,
            Version = "v" + metadata.LatestBuild.Version, RevisionId = "rpcs3:" + match.Groups["hash"].Value,
            PublishedAt = published, IsPrerelease = true,
            ReleasePage = new($"https://github.com/RPCS3/{repository}/releases/tag/{tag}"),
            AssetName = Path.GetFileName(download.AbsolutePath), DownloadUrl = download, SizeBytes = package.Size,
            PackageFormat = platform == PlatformKind.Windows ? EmulatorPackageFormat.SevenZip : EmulatorPackageFormat.AppImage,
            Sha256 = package.Checksum.ToLowerInvariant()
        };
    }

    private static void ValidateChannel(EmulatorReleaseChannel channel)
    {
        if (channel.Id != ChannelId || channel.IsStable || channel.SupportedPlatforms is null ||
            !channel.SupportedPlatforms.Contains(PlatformKind.Windows) ||
            !channel.SupportedPlatforms.Contains(PlatformKind.Linux) ||
            channel.Source is not { Kind: EmulatorReleaseSourceKind.ProjectWebsite, Url: ProjectUrl,
                Prereleases: EmulatorPrereleasePolicy.Any, Tag: null, TagPrefix: null })
            throw new InvalidDataException("This adapter requires the official RPCS3 Rolling channel.");
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
    private sealed record UpdateResponse(int ReturnCode, LatestBuild? LatestBuild);
    private sealed record LatestBuild(string Datetime, string Version, Package? Windows, Package? Linux);
    private sealed record Package(string Download, long Size, string Checksum);
}
