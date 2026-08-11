namespace CartLaunchCompanion.Core.Updating;

public sealed record RuntimeUpdateRequest(
    string CartRoot,
    string Platform,
    string StagedRuntimeRoot,
    string ManifestPath);

public sealed record RuntimeUpdateResult(
    string ActiveRuntimeRoot,
    string EntryPoint,
    string PreviousRuntimeRoot,
    string Version);
