using System.ComponentModel;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed record Pcsx2SetupChoice(PlatformKind? Platform, string DisplayName);
public sealed record Pcsx2ResolutionOption(int Multiplier, string DisplayName);
public sealed record Pcsx2RendererOption(int Value, string DisplayName);
public sealed record Pcsx2AspectOption(string Value, string DisplayName);

public sealed class Pcsx2SetupViewModel(Pcsx2SetupService service, Pcsx2ConfigurationAdapter configuration,
    string? mediaRoot = null, string? stateRoot = null) : INotifyPropertyChanged
{
    private readonly EmulatorSetupPreferencesStore _preferences = new(stateRoot ?? AppContext.BaseDirectory);
    private Pcsx2SetupChoice? _selectedChoice;
    private SimpleEmulationPreset? _selectedProfile;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string MediaRoot { get; } = mediaRoot ?? AppContext.BaseDirectory;
    public string StateRoot { get; } = stateRoot ?? AppContext.BaseDirectory;
    public IReadOnlyList<Pcsx2SetupStatus> Installations { get; private set; } = [];
    public IReadOnlyList<Pcsx2SetupChoice> PlatformChoices { get; private set; } = [];
    public Pcsx2SetupChoice? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (_selectedChoice == value) return;
            _selectedChoice = value;
            UpdateRendererOptions();
            PreviewChanges = [];
            Message = Guidance;
            Notify();
        }
    }
    public IReadOnlyList<SimpleEmulationPreset> Profiles => configuration.Presets;
    public SimpleEmulationPreset? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_selectedProfile == value) return;
            _selectedProfile = value;
            if (value is not null) LoadProfile(value);
            PreviewChanges = [];
            Notify();
        }
    }
    public IReadOnlyList<Pcsx2ResolutionOption> ResolutionOptions { get; } =
        [new(1, "Native (1×)"), new(2, "720p-class (2×)"), new(3, "1080p-class (3×)"),
         new(4, "1440p-class (4×)"), new(6, "4K-class (6×)")];
    private static readonly IReadOnlyList<Pcsx2RendererOption> AllRenderers =
        [new(-1, "Automatic"), new(14, "Vulkan"), new(12, "OpenGL"), new(15, "Direct3D 12"), new(3, "Direct3D 11")];
    public IReadOnlyList<Pcsx2RendererOption> RendererOptions { get; private set; } = AllRenderers;
    public IReadOnlyList<Pcsx2AspectOption> AspectOptions { get; } =
        [new("Auto 4:3/3:2", "Automatic / original"), new("4:3", "4:3"), new("16:9", "16:9 widescreen"), new("Stretch", "Stretch to display")];
    public Pcsx2ResolutionOption SelectedResolution { get; set; } = null!;
    public Pcsx2RendererOption SelectedRenderer { get; set; } = null!;
    public Pcsx2AspectOption SelectedAspect { get; set; } = null!;
    public bool StartFullscreen { get; set; } = true;
    public bool Vsync { get; set; }
    public bool WidescreenPatches { get; set; }
    public IReadOnlyList<string> PreviewChanges { get; private set; } = [];
    public bool HasPreview => PreviewChanges.Count > 0;
    public bool CanApplyConfiguration => HasInstallation && SelectedProfile is not null;
    public bool CanRestoreConfiguration { get; private set; }
    public string ProfileDescription => SelectedProfile?.Description ?? "";
    public string ApplyActionLabel => SelectedInstallations.Count > 1 ? "Apply settings to both" : "Apply settings";
    public string Message { get; private set; } = "Checking PCSX2 setup…";
    private IReadOnlyList<Pcsx2SetupStatus> SelectedInstallations => SelectedChoice?.Platform is { } platform
        ? Installations.Where(item => item.Platform == platform).ToArray() : Installations;
    private Pcsx2SetupStatus? LaunchInstallation => SelectedInstallations.FirstOrDefault(item =>
        item.Platform == (OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Windows));
    public string State => SelectedInstallations.Count == 0 ? "Not installed" :
        SelectedInstallations.Any(item => !item.BiosFound) ? "BIOS needed" :
        SelectedInstallations.Any(item => !item.SettingsCreated) ? "Finish setup in PCSX2" : "Ready to test";
    public string BiosStatus => SelectedInstallations.Count > 1
        ? SelectedInstallations.All(item => item.BiosFound) ? "BIOS file found for Windows and Linux" :
            "BIOS is missing for " + string.Join(" and ", SelectedInstallations.Where(item => !item.BiosFound).Select(item => item.Platform))
        : SelectedInstallations.FirstOrDefault()?.BiosFound == true ? "BIOS file found" : "BIOS is missing";
    public string SettingsStatus => SelectedInstallations.Count > 1
        ? SelectedInstallations.All(item => item.SettingsCreated) ? "Portable settings found for Windows and Linux" :
            "First-run settings still needed for " + string.Join(" and ", SelectedInstallations.Where(item => !item.SettingsCreated).Select(item => item.Platform))
        : SelectedInstallations.FirstOrDefault()?.SettingsCreated == true ? "Portable settings found" : "First-run settings not created";
    public string BiosFolder => string.Join(Environment.NewLine, SelectedInstallations.Select(item => $"{item.Platform}: {item.BiosFolder}"));
    public bool HasInstallation => SelectedInstallations.Count > 0;
    public bool CanLaunch => LaunchInstallation is not null;
    public string LaunchActionLabel => LaunchInstallation is null ? "Open PCSX2 setup on this platform" : $"Open {LaunchInstallation.Platform} PCSX2 setup";
    public Pcsx2SetupStatus? CurrentPlatformInstallation => LaunchInstallation;
    private string Guidance => SelectedInstallations.Count == 0 ? "PCSX2 is not installed." :
        SelectedInstallations.Any(item => !item.BiosFound)
            ? SelectedInstallations.Count > 1
                ? "Import your BIOS once and it will be copied to both portable installations."
                : "Import a BIOS dumped from your own PlayStation 2 console."
            : SelectedInstallations.Any(item => !item.SettingsCreated)
                ? "Apply a simple preset, then open PCSX2 on each operating system to verify its BIOS and controller."
                : "Portable settings and a BIOS were found. Open PCSX2 for a final controller check, then launch a game.";

    public async Task RefreshAsync(CancellationToken token = default)
    {
        var previous = SelectedChoice?.Platform;
        Installations = await service.InspectAsync(token);
        var choices = Installations.Select(item => new Pcsx2SetupChoice(item.Platform, item.Platform + " x64")).ToList();
        if (Installations.Count > 1) choices.Add(new(null, "Both"));
        PlatformChoices = choices;
        SelectedChoice = PlatformChoices.FirstOrDefault(item => item.Platform == previous)
            ?? PlatformChoices.FirstOrDefault(item => item.Platform is null)
            ?? PlatformChoices.FirstOrDefault(item => item.Platform == (OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Windows))
            ?? PlatformChoices.FirstOrDefault();
        if (SelectedProfile is null)
            SelectedProfile = await PreferredProfileAsync(token);
        await PreviewConfigurationAsync(token);
        Notify();
    }

    public async Task ImportAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        var targets = SelectedInstallations.Select(item => item.Platform).ToArray();
        if (targets.Length == 0) return;
        var updated = await service.ImportBiosAsync(targets, paths, token);
        Installations = Installations.Select(item => updated.FirstOrDefault(value => value.Platform == item.Platform) ?? item).ToArray();
        Message = targets.Length > 1
            ? "BIOS copied to both portable PCSX2 installations. PCSX2 will validate it when opened."
            : "BIOS copied to the portable PCSX2 folder. PCSX2 will validate it when opened.";
        Notify();
    }

    public void Report(string message) { Message = message; Notify(); }
    public async Task PreviewConfigurationAsync(CancellationToken token = default)
    {
        if (!CanApplyConfiguration) { PreviewChanges = []; CanRestoreConfiguration = false; Notify(); return; }
        var profile = CustomizedProfile();
        var previews = new List<EmulatorConfigurationPreview>();
        foreach (var target in SelectedInstallations)
            previews.Add(await configuration.PreviewAsync(target.Platform, profile, token));
        PreviewChanges = previews.SelectMany(preview => preview.Changes.Select(change =>
            SelectedInstallations.Count > 1 ? $"{preview.Platform}: {change.Summary}" : change.Summary)).ToArray();
        CanRestoreConfiguration = previews.Any(preview => preview.CanRestore);
        Message = PreviewChanges.Count == 0
            ? "These recommended settings are already applied."
            : $"Review the {PreviewChanges.Count} setting changes below. Controller bindings and unrelated settings are left alone.";
        Notify();
    }

    public async Task ApplyConfigurationAsync(CancellationToken token = default)
    {
        if (!CanApplyConfiguration) return;
        var profile = CustomizedProfile();
        var targets = SelectedInstallations.ToArray();
        // Validate all target paths and changes before modifying either platform.
        foreach (var target in targets) await configuration.PreviewAsync(target.Platform, profile, token);
        var applied = new List<PlatformKind>();
        try
        {
            foreach (var target in targets)
            {
                await configuration.ApplyAsync(target.Platform, profile, token);
                applied.Add(target.Platform);
            }
        }
        catch
        {
            foreach (var platform in applied.AsEnumerable().Reverse())
                try { await configuration.RestoreLatestAsync(platform, CancellationToken.None); } catch (IOException) { }
            throw;
        }
        Message = targets.Length > 1
            ? "Recommended settings applied to Windows and Linux. Controller mappings remain separate for each system."
            : $"Recommended settings applied to {targets[0].Platform}.";
        await _preferences.SaveSelectedPresetAsync(SelectedProfile!.Id, token);
        await RefreshAsync(token);
    }

    private async Task<SimpleEmulationPreset> PreferredProfileAsync(CancellationToken token)
    {
        var saved = await _preferences.LoadAsync(token);
        if (saved.SelectedPresetId is { } id) return SimpleEmulationPresetCatalog.Get(id);
        if (SelectedInstallations.Count == 0) return SimpleEmulationPresetCatalog.Get("balanced");
        var ranked = new List<(SimpleEmulationPreset Preset, int Changes)>();
        foreach (var preset in Profiles)
        {
            var translated = configuration.Translate(preset);
            var count = 0;
            foreach (var target in SelectedInstallations)
                count += (await configuration.PreviewAsync(target.Platform, translated, token)).Changes.Count;
            ranked.Add((preset, count));
        }
        return ranked.OrderBy(item => item.Changes)
            .ThenBy(item => item.Preset.Id == "balanced" ? 0 : 1)
            .First().Preset;
    }

    public async Task RestoreConfigurationAsync(CancellationToken token = default)
    {
        var restored = false;
        foreach (var target in SelectedInstallations)
            restored |= await configuration.RestoreLatestAsync(target.Platform, token);
        Message = restored ? "The previous PCSX2 configuration was restored." : "No CLC configuration backup is available.";
        await RefreshAsync(token);
    }

    private EmulatorConfigurationProfile CustomizedProfile() => configuration.Customize(SelectedProfile!,
        SelectedResolution.Multiplier, SelectedRenderer.Value, SelectedAspect.Value,
        StartFullscreen, Vsync, WidescreenPatches);

    private void LoadProfile(SimpleEmulationPreset preset)
    {
        var profile = configuration.Translate(preset);
        string Value(string section, string key) => profile.Values.Single(item => item.Section == section && item.Key == key).Value;
        SelectedResolution = ResolutionOptions.Single(option => option.Multiplier.ToString() == Value("EmuCore/GS", "upscale_multiplier"));
        SelectedRenderer = RendererOptions.Single(option => option.Value.ToString() == Value("EmuCore/GS", "Renderer"));
        SelectedAspect = AspectOptions.Single(option => option.Value == Value("EmuCore/GS", "AspectRatio"));
        StartFullscreen = Value("UI", "StartFullscreen") == "true";
        Vsync = Value("EmuCore/GS", "VsyncEnable") == "1";
        WidescreenPatches = Value("EmuCore", "EnableWideScreenPatches") == "true";
    }
    private void UpdateRendererOptions()
    {
        RendererOptions = SelectedInstallations.Any(item => item.Platform == PlatformKind.Linux)
            ? AllRenderers.Where(option => option.Value is not (3 or 15)).ToArray()
            : AllRenderers;
        if (SelectedRenderer is null || !RendererOptions.Contains(SelectedRenderer))
            SelectedRenderer = RendererOptions[0];
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
