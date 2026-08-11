namespace CartLaunchCompanion.Core.Updating;

public sealed record RuntimeUpdateAvailability(
    string Version,
    Uri ManifestUri,
    Uri PayloadUri,
    long PayloadBytes,
    string ReleasePage);

public sealed record PreparedRuntimeUpdate(
    string Version,
    string Platform,
    string StagedRuntimeRoot,
    string ManifestPath);

public interface IRuntimeUpdateService
{
    Task<RuntimeUpdateAvailability?> CheckAsync(
        Version currentVersion,
        string platform,
        CancellationToken cancellationToken = default);

    Task<PreparedRuntimeUpdate> DownloadAndPrepareAsync(
        RuntimeUpdateAvailability update,
        string cartRoot,
        string platform,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
