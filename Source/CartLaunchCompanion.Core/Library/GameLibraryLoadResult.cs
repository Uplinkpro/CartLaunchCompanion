namespace CartLaunchCompanion.Core.Library;

public sealed class GameLibraryLoadResult
{
    public List<GameLibraryEntry> Games { get; } = [];
    public List<GameLibraryFailure> Failures { get; } = [];

    public bool HasGames => Games.Count > 0;
}

public sealed record GameLibraryFailure(
    string FolderPath,
    string? ConfigurationPath,
    string Message,
    Exception? Exception = null);
