namespace CartLaunchCompanion.Core.Emulators;

/// <summary>
/// Emulator-neutral choices shown to players. Emulator adapters translate these
/// goals into their own configuration keys and supported values.
/// </summary>
public enum EmulationPerformanceGoal { Compatibility, Balanced, VisualQuality }
public enum EmulationResolutionGoal { Original, Hd, FullHd, UltraHd }
public enum EmulationAspectGoal { Original, Automatic, Widescreen }

public sealed record SimpleEmulationPreset(
    int SchemaVersion,
    string Id,
    string DisplayName,
    string Description,
    EmulationPerformanceGoal Performance,
    EmulationResolutionGoal Resolution,
    EmulationAspectGoal Aspect,
    bool StartFullscreen,
    bool Vsync,
    bool WidescreenEnhancements);

public static class SimpleEmulationPresetCatalog
{
    public const int CurrentSchemaVersion = 1;

    public static IReadOnlyList<SimpleEmulationPreset> All { get; } =
    [
        new(CurrentSchemaVersion, "compatibility", "Compatibility",
            "Prioritizes reliable play and original presentation for slower hardware or difficult games.",
            EmulationPerformanceGoal.Compatibility, EmulationResolutionGoal.Original,
            EmulationAspectGoal.Original, true, false, false),
        new(CurrentSchemaVersion, "balanced", "Balanced",
            "A clear picture with modest enhancements. Recommended for most systems.",
            EmulationPerformanceGoal.Balanced, EmulationResolutionGoal.Hd,
            EmulationAspectGoal.Automatic, true, false, false),
        new(CurrentSchemaVersion, "quality", "Quality",
            "Uses higher image quality and widescreen enhancements when the emulator supports them.",
            EmulationPerformanceGoal.VisualQuality, EmulationResolutionGoal.FullHd,
            EmulationAspectGoal.Widescreen, true, false, true),
        new(CurrentSchemaVersion, "four-k", "4K",
            "Targets a 4K display and is intended for powerful desktop systems.",
            EmulationPerformanceGoal.VisualQuality, EmulationResolutionGoal.UltraHd,
            EmulationAspectGoal.Widescreen, true, false, true)
    ];

    public static SimpleEmulationPreset Get(string id) =>
        All.Single(preset => preset.Id.Equals(id, StringComparison.Ordinal));
}
