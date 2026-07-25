using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CartLaunchCompanion.Models;

namespace CartLaunchCompanion.Services;

public sealed class SteamService
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public SteamService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CartLaunchCompanion/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://store.steampowered.com/");
    }

    public async Task<SteamMetadata?> GetMetadataAsync(string steamId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return null;
        var url = $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(steamId)}&l=english&cc=us";
        using var response = await _http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!doc.RootElement.TryGetProperty(steamId, out var app) ||
            !app.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
            !app.TryGetProperty("data", out var data)) return null;

        var trailerUrls = GetTrailers(data).ToList();
        if (trailerUrls.Count == 0)
        {
            var scraped = await GetTrailersFromStorePageAsync(steamId, token);
            trailerUrls.AddRange(scraped.Where(url => !trailerUrls.Contains(url, StringComparer.OrdinalIgnoreCase)));
        }
        return new SteamMetadata
        {
            Description = GetString(data, "short_description"),
            Developer = FirstArrayValue(data, "developers"),
            Publisher = FirstArrayValue(data, "publishers"),
            Genre = JoinDescriptions(data, "genres"),
            ReleaseDate = data.TryGetProperty("release_date", out var rd) ? GetString(rd, "date") : "",
            Website = GetString(data, "website"),
            TrailerUrl = trailerUrls.FirstOrDefault() ?? "",
            TrailerUrls = trailerUrls
        };
    }

    public async Task<string> CacheFirstAvailableTrailerAsync(
        IEnumerable<string> trailerUrls,
        string steamId,
        CancellationToken token = default)
    {
        Exception? lastError = null;
        foreach (var trailerUrl in trailerUrls.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var cached = await CacheTrailerAsync(trailerUrl, steamId, token);
                if (!string.IsNullOrWhiteSpace(cached)) return cached;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw new InvalidOperationException("Steam did not provide a downloadable trailer variant.", lastError);
        return "";
    }

    public async Task<string> CacheTrailerAsync(string trailerUrl, string steamId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(trailerUrl) || string.IsNullOrWhiteSpace(steamId)) return "";

        var normalized = NormalizeSteamMediaUrl(trailerUrl);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return "";

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (!string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
            extension = ".mp4";

        var cacheFolder = Path.Combine(
            PortablePaths.DataDirectory, "TrailerCache");
        Directory.CreateDirectory(cacheFolder);

        var urlKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12];
        var destination = Path.Combine(cacheFolder, $"steam-{steamId}-{urlKey}{extension}");
        var temporary = destination + ".download";

        if (File.Exists(destination) && new FileInfo(destination).Length > 64 * 1024)
            return destination;

        if (File.Exists(temporary)) File.Delete(temporary);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Referrer = new Uri($"https://store.steampowered.com/app/{Uri.EscapeDataString(steamId)}/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("video/mp4"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("video/webm"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
        {
            await input.CopyToAsync(output, token);
        }

        var length = new FileInfo(temporary).Length;
        if (length < 64 * 1024)
        {
            File.Delete(temporary);
            throw new InvalidDataException($"Steam returned an incomplete trailer ({length} bytes).");
        }

        File.Move(temporary, destination, true);
        return destination;
    }

    private async Task<IReadOnlyList<string>> GetTrailersFromStorePageAsync(string steamId, CancellationToken token)
    {
        try
        {
            var url = $"https://store.steampowered.com/app/{Uri.EscapeDataString(steamId)}/?l=english&cc=us&agecheckage=1-January-1980";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://store.steampowered.com/");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(token);

            var decoded = WebUtility.HtmlDecode(html)
                .Replace("\\/", "/", StringComparison.Ordinal)
                .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase);

            var results = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         decoded,
                         @"https?://[^""'<>\s]+\.(?:mpd|m3u8|mp4|webm)(?:\?[^""'<>\s]*)?",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var candidate = NormalizeSteamMediaUrl(match.Value);
                if ((candidate.Contains("steamstatic", StringComparison.OrdinalIgnoreCase) ||
                     candidate.Contains("akamaihd", StringComparison.OrdinalIgnoreCase) ||
                     candidate.Contains("steampowered", StringComparison.OrdinalIgnoreCase)) &&
                    !results.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(candidate);
                }
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> GetTrailers(JsonElement data)
    {
        if (!data.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array) return [];

        var results = new List<string>();
        var ordered = movies.EnumerateArray()
            .OrderByDescending(movie => movie.TryGetProperty("highlight", out var h) && h.ValueKind == JsonValueKind.True);

        foreach (var movie in ordered)
        {
            // Steam's newer trailer payloads may place DASH/HLS manifests in
            // fields that differ from the legacy mp4/webm objects. Walk the
            // complete movie object so new nested streaming fields are retained.
            CollectTrailerStrings(movie, results);
        }

        return results
            .OrderBy(url => IsAdaptiveManifest(url) ? 0 : 1)
            .ThenBy(url => url.Contains("microtrailer", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();
    }

    private static void CollectTrailerStrings(JsonElement element, List<string> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectTrailerStrings(property.Value, results);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectTrailerStrings(item, results);
                break;
            case JsonValueKind.String:
                var candidate = NormalizeSteamMediaUrl(element.GetString() ?? "");
                if (IsTrailerMedia(candidate) &&
                    !results.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    results.Add(candidate);
                break;
        }
    }

    private static bool IsTrailerMedia(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath;
        return path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdaptiveManifest(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeSteamMediaUrl(string value)
    {
        value = value.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal)) value = "https:" + value;
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value[7..];
        return value;
    }

    private static string GetString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string FirstArrayValue(JsonElement node, string name) =>
        node.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().FirstOrDefault().GetString() ?? "" : "";

    private static string JoinDescriptions(JsonElement node, string name) =>
        node.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? string.Join(", ", values.EnumerateArray().Select(v => GetString(v, "description")).Where(s => s.Length > 0)) : "";
}
