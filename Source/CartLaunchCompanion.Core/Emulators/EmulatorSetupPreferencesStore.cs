namespace CartLaunchCompanion.Core.Emulators;

public sealed record EmulatorSetupPreferences
{
    public int SchemaVersion { get; init; } = 1;
    public string? SelectedPresetId { get; init; }
}

/// <summary>Stores the library-wide playstyle used by every emulator adapter.</summary>
public sealed class EmulatorSetupPreferencesStore(string stateRoot)
{
    private const string RelativePath = "Config/EmulatorCompanion/setup-preferences.json";

    public async Task<EmulatorSetupPreferences> LoadAsync(CancellationToken token = default)
    {
        var preferences = await EmulatorUpdateFiles.ReadAsync<EmulatorSetupPreferences>(
            EmulatorPathContract.Resolve(stateRoot, RelativePath), token) ?? new();
        Validate(preferences);
        return preferences;
    }

    public async Task SaveSelectedPresetAsync(string presetId, CancellationToken token = default)
    {
        _ = SimpleEmulationPresetCatalog.Get(presetId);
        var path = EmulatorPathContract.Resolve(stateRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        await LoadAsync(token);
        await EmulatorUpdateFiles.WriteAsync(path,
            new EmulatorSetupPreferences { SelectedPresetId = presetId }, token);
    }

    private static void Validate(EmulatorSetupPreferences preferences)
    {
        if (preferences.SchemaVersion != 1 ||
            preferences.SelectedPresetId is { } id &&
            !SimpleEmulationPresetCatalog.All.Any(preset => preset.Id == id))
            throw new InvalidDataException("Unsupported emulator setup preferences. Existing settings were left unchanged.");
    }
}
