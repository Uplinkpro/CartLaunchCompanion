using System.Text.Json;
using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Metadata;

public sealed class OpenGameMetadataService(HttpClient httpClient)
{
    public async Task<OpenGameMetadataResult> FillMissingAsync(
        GameConfiguration configuration,
        string? steamAppId = null,
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
            result.UsedWikipedia = await FillFromWikipediaAsync(configuration, query, cancellationToken);
        }

        return result;
    }

    private async Task<(string PageName, string CoverUrl)> FillFromPcGamingWikiAsync(
        GameConfiguration configuration,
        string? steamAppId,
        CancellationToken cancellationToken)
    {
        var fields = "Infobox_game._pageName=Page,Infobox_game.Developers," +
                     "Infobox_game.Publishers,Infobox_game.Released," +
                     "Infobox_game.Genres,Infobox_game.Cover_URL";
        string where;
        if (uint.TryParse(steamAppId, out _))
        {
            where = $"Infobox_game.Steam_AppID HOLDS \"{steamAppId}\"";
        }
        else
        {
            var pageName = await FindPcGamingWikiPageAsync(configuration.Game.Name, cancellationToken);
            if (pageName.Length == 0) return ("", "");
            where = $"Infobox_game._pageName=\"{pageName.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }
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

    private async Task<string> FindPcGamingWikiPageAsync(string gameName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return "";
        var url = "https://www.pcgamingwiki.com/w/api.php?action=query&list=search" +
                  "&srsearch=" + Uri.EscapeDataString($"\"{gameName.Trim()}\"") +
                  "&srnamespace=0&srlimit=5&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("search", out var matches)) return "";

        var titles = matches.EnumerateArray()
            .Select(item => Read(item, "title"))
            .Where(title => title.Length > 0)
            .ToArray();
        return titles.FirstOrDefault(title =>
                   string.Equals(title, gameName.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? titles.FirstOrDefault()
               ?? "";
    }

    private async Task<bool> FillFromWikipediaAsync(
        GameConfiguration configuration,
        string title,
        CancellationToken cancellationToken)
    {
        var search = $"\"{title}\" video game";
        var url = "https://en.wikipedia.org/w/api.php?action=query&generator=search" +
                  "&gsrsearch=" + Uri.EscapeDataString(search) +
                  "&gsrlimit=1&prop=extracts|pageprops&exintro=1&explaintext=1&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages)) return false;
        var page = pages.EnumerateObject().Select(item => item.Value).FirstOrDefault();
        var extract = Read(page, "extract");
        configuration.Game.Description = Fill(configuration.Game.Description, extract);

        var wikidataId = page.ValueKind == JsonValueKind.Object &&
                         page.TryGetProperty("pageprops", out var pageProps)
            ? Read(pageProps, "wikibase_item")
            : "";
        var usedWikidata = await FillFromWikidataAsync(configuration, wikidataId, cancellationToken);
        return extract.Length > 0 || usedWikidata;
    }

    private async Task<bool> FillFromWikidataAsync(
        GameConfiguration configuration,
        string entityId,
        CancellationToken cancellationToken)
    {
        if (entityId.Length == 0) return false;
        var url = "https://www.wikidata.org/w/api.php?action=wbgetentities&ids=" +
                  Uri.EscapeDataString(entityId) + "&props=claims&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("entities", out var entities) ||
            !entities.TryGetProperty(entityId, out var entity) ||
            !entity.TryGetProperty("claims", out var claims)) return false;

        var developerIds = ReadClaimEntityIds(claims, "P178");
        var publisherIds = ReadClaimEntityIds(claims, "P123");
        var genreIds = ReadClaimEntityIds(claims, "P136");
        var allIds = developerIds.Concat(publisherIds).Concat(genreIds).Distinct().ToArray();
        var labels = await ReadWikidataLabelsAsync(allIds, cancellationToken);

        configuration.Game.Developer = Fill(configuration.Game.Developer, JoinLabels(developerIds, labels));
        configuration.Game.Publisher = Fill(configuration.Game.Publisher, JoinLabels(publisherIds, labels));
        configuration.Game.Genre = Fill(configuration.Game.Genre, JoinLabels(genreIds, labels));
        configuration.Game.ReleaseDate = Fill(configuration.Game.ReleaseDate, ReadEarliestDate(claims, "P577"));
        return allIds.Length > 0 || !string.IsNullOrWhiteSpace(configuration.Game.ReleaseDate);
    }

    private async Task<Dictionary<string, string>> ReadWikidataLabelsAsync(
        IReadOnlyCollection<string> entityIds,
        CancellationToken cancellationToken)
    {
        if (entityIds.Count == 0) return new Dictionary<string, string>();
        var url = "https://www.wikidata.org/w/api.php?action=wbgetentities&ids=" +
                  Uri.EscapeDataString(string.Join('|', entityIds)) +
                  "&props=labels&languages=en|mul&languagefallback=1&format=json";
        using var document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("entities", out var entities)) return new Dictionary<string, string>();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in entities.EnumerateObject())
        {
            if (!item.Value.TryGetProperty("labels", out var labels)) continue;
            var label = labels.TryGetProperty("en", out var english) ? Read(english, "value")
                : labels.TryGetProperty("mul", out var multilingual) ? Read(multilingual, "value") : "";
            if (label.Length > 0) result[item.Name] = label;
        }
        return result;
    }

    private static string[] ReadClaimEntityIds(JsonElement claims, string property)
    {
        if (!claims.TryGetProperty(property, out var statements) || statements.ValueKind != JsonValueKind.Array)
            return [];
        return statements.EnumerateArray()
            .Select(statement => statement.TryGetProperty("mainsnak", out var snak) &&
                                 snak.TryGetProperty("datavalue", out var dataValue) &&
                                 dataValue.TryGetProperty("value", out var value)
                ? Read(value, "id") : "")
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ReadEarliestDate(JsonElement claims, string property)
    {
        if (!claims.TryGetProperty(property, out var statements) || statements.ValueKind != JsonValueKind.Array)
            return "";
        return statements.EnumerateArray()
            .Select(statement => statement.TryGetProperty("mainsnak", out var snak) &&
                                 snak.TryGetProperty("datavalue", out var dataValue) &&
                                 dataValue.TryGetProperty("value", out var value)
                ? Read(value, "time").TrimStart('+') : "")
            .Where(value => value.Length >= 10)
            .Select(value => value[..10])
            .OrderBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault() ?? "";
    }

    private static string JoinLabels(IEnumerable<string> ids, IReadOnlyDictionary<string, string> labels) =>
        string.Join(", ", ids.Select(id => labels.GetValueOrDefault(id, "")).Where(label => label.Length > 0));

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CartLaunchCompanion/2.3 metadata-configurator");
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
