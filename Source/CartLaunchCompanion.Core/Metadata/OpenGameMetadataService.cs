using System.Text.Json;
using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Metadata;

public sealed class OpenGameMetadataService(HttpClient httpClient)
{
    public async Task<OpenGameMetadataResult> FillMissingAsync(
        GameConfiguration configuration,
        string steamAppId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var result = new OpenGameMetadataResult();
        var pcGamingWiki = await FillFromPcGamingWikiAsync(configuration, steamAppId, cancellationToken);
        var pageName = pcGamingWiki.PageName;
        result.UsedPcGamingWiki = pageName.Length > 0;
        result.CoverUrl = pcGamingWiki.CoverUrl;

        if (string.IsNullOrWhiteSpace(configuration.Game.Description))
        {
            var query = pageName.Length > 0 ? pageName : configuration.Game.Name;
            result.UsedWikipedia = await FillDescriptionFromWikipediaAsync(configuration, query, cancellationToken);
        }

        return result;
    }

    private async Task<(string PageName, string CoverUrl)> FillFromPcGamingWikiAsync(
        GameConfiguration configuration,
        string steamAppId,
        CancellationToken cancellationToken)
    {
        if (!uint.TryParse(steamAppId, out _)) return ("", "");
        var fields = "Infobox_game._pageName=Page,Infobox_game.Developers," +
                     "Infobox_game.Publishers,Infobox_game.Released," +
                     "Infobox_game.Genres,Infobox_game.Cover_URL";
        var where = $"Infobox_game.Steam_AppID HOLDS \"{steamAppId}\"";
        var url = "https://www.pcgamingwiki.com/w/api.php?action=cargoquery" +
                  "&tables=Infobox_game&fields=" + Uri.EscapeDataString(fields) +
                  "&where=" + Uri.EscapeDataString(where) + "&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("cargoquery", out var rows) || rows.GetArrayLength() == 0 ||
            !rows[0].TryGetProperty("title", out var title)) return ("", "");

        configuration.Game.Developer = Fill(configuration.Game.Developer, CleanCompanies(Read(title, "Developers")));
        configuration.Game.Publisher = Fill(configuration.Game.Publisher, CleanCompanies(Read(title, "Publishers")));
        configuration.Game.Genre = Fill(configuration.Game.Genre, CleanList(Read(title, "Genres")));
        configuration.Game.ReleaseDate = Fill(configuration.Game.ReleaseDate,
            Read(title, "Released").Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
        return (Read(title, "Page"), Read(title, "Cover URL"));
    }

    private async Task<bool> FillDescriptionFromWikipediaAsync(
        GameConfiguration configuration,
        string title,
        CancellationToken cancellationToken)
    {
        var search = $"\"{title}\" video game";
        var url = "https://en.wikipedia.org/w/api.php?action=query&generator=search" +
                  "&gsrsearch=" + Uri.EscapeDataString(search) +
                  "&gsrlimit=1&prop=extracts&exintro=1&explaintext=1&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages)) return false;
        var page = pages.EnumerateObject().Select(item => item.Value).FirstOrDefault();
        var extract = Read(page, "extract");
        if (extract.Length == 0) return false;
        configuration.Game.Description = extract;
        return true;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/1.0 metadata-configurator");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string Read(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    private static string Fill(string current, string candidate) => string.IsNullOrWhiteSpace(current) ? candidate : current;
    private static string CleanCompanies(string value) => CleanList(value.Replace("Company:", "", StringComparison.Ordinal));
    private static string CleanList(string value) => string.Join(", ", value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}

public sealed class OpenGameMetadataResult
{
    public bool UsedPcGamingWiki { get; set; }
    public bool UsedWikipedia { get; set; }
    public string CoverUrl { get; set; } = "";
    public bool UsedAny => UsedPcGamingWiki || UsedWikipedia;
}
