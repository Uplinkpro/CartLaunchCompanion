namespace CartLaunchCompanion.Core.Library;

public interface IGamePathResolver
{
    string Resolve(string gameFolder, string configuredPath);
    string? ResolveExisting(string gameFolder, string configuredPath);
}
