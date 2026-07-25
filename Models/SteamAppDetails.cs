namespace CartLaunchCompanion.Models;

public sealed class SteamMetadata
{
    public string Description { get; init; } = "";
    public string Developer { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Genre { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string Website { get; init; } = "";
    public string TrailerUrl { get; init; } = "";
    public IReadOnlyList<string> TrailerUrls { get; init; } = [];
}
