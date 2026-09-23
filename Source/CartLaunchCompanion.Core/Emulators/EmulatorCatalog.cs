namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Versioned catalog metadata; it contains no local installation state.</summary>
public sealed record EmulatorCatalog
{
    public const int CurrentSchemaVersion = 2;
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<EmulatorDefinition> Emulators { get; init; }
}
