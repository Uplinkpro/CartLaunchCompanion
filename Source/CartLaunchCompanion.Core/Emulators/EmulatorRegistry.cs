namespace CartLaunchCompanion.Core.Emulators;

/// <summary>One installation per emulator ID and operating system.</summary>
public sealed record EmulatorRegistry
{
    public const int CurrentSchemaVersion = 1;
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<EmulatorInstallation> Installations { get; init; }

    public static EmulatorRegistry Empty => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Installations = []
    };
}
