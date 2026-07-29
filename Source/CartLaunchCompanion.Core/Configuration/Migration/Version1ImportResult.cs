namespace CartLaunchCompanion.Core.Configuration.Migration;

public sealed class Version1ImportResult
{
    public required GameConfiguration Configuration { get; init; }

    public List<string> ImportedFields { get; } = [];

    public List<string> UnmappedFields { get; } = [];

    public List<string> Warnings { get; } = [];
}
