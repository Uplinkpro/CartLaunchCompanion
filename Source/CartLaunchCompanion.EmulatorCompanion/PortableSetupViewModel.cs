using System.ComponentModel;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed record PortablePlatformChoice(PlatformKind? Platform, string DisplayName);

public sealed class PortableSetupViewModel(PortableEmulatorSetupService service) : INotifyPropertyChanged
{
    private readonly GameContentImportService _contentImport = new(service.MediaRoot, service.StateRoot, service.Definition.EmulatorId);
    private readonly EmulatorSetupPreferencesStore _preferences = new(service.StateRoot);
    private PortablePlatformChoice? _selectedPlatform;
    private SimpleEmulationPreset? _selectedPreset;
    private bool _isExternalAppRunning;
    private bool _previewCanApply;
    private bool _isContentBusy;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ManagedSetupDefinition Definition => service.Definition;
    public string MediaRoot => service.MediaRoot;
    public string StateRoot => service.StateRoot;
    public string Title => $"Set up {Definition.DisplayName}";
    public string Requirement => Definition.Requirement;
    public string AutomationSummary => Definition.AutomationSummary;
    public IReadOnlyList<PortableSetupTarget> Targets { get; private set; } = [];
    public IReadOnlyList<PortablePlatformChoice> PlatformChoices { get; private set; } = [];
    public IReadOnlyList<SimpleEmulationPreset> Presets => SimpleEmulationPresetCatalog.All;
    public IReadOnlyList<string> Changes { get; private set; } = [];
    public bool HasChanges => Changes.Count > 0;
    public string Message { get; private set; } = "Checking setup…";
    public IReadOnlyList<string> ContentItems { get; private set; } = [];
    public bool SupportsContentImport => _contentImport.IsSupported;
    public bool HasContentItems => ContentItems.Count > 0;
    public string ContentMessage { get; private set; } = "Checking game updates and DLC…";
    public bool CanImportContent { get; private set; }
    public string ImportContentLabel => _isContentBusy ? "Importing content…" : "Import updates and DLC";
    public bool CanApply => Definition.CanApplyAutomatically && _previewCanApply &&
        SelectedTargets.Count > 0 && SelectedPreset is not null && Changes.Count > 0;
    public bool CanOpen => CurrentHostTarget is not null && !_isExternalAppRunning;
    public string ApplyLabel => SelectedTargets.Count > 1 ? "Apply setup to both" : "Apply setup";
    public string OpenLabel => _isExternalAppRunning ? $"Waiting for {Definition.DisplayName} to close…"
        : CurrentHostTarget is null ? "Open on this platform" : $"Open {Definition.DisplayName} on {CurrentHostTarget.Platform}";
    public PortablePlatformChoice? SelectedPlatform
    {
        get => _selectedPlatform;
        set { if (_selectedPlatform == value) return; _selectedPlatform = value; _ = RefreshPreviewsAsync(); Notify(); }
    }
    public SimpleEmulationPreset? SelectedPreset
    {
        get => _selectedPreset;
        set { if (_selectedPreset == value) return; _selectedPreset = value; _ = PreviewAsync(); Notify(); }
    }
    public PortableSetupTarget? CurrentHostTarget => SelectedTargets.FirstOrDefault(item =>
        item.Platform == (OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Windows));
    private IReadOnlyList<PortableSetupTarget> SelectedTargets => SelectedPlatform?.Platform is { } platform
        ? Targets.Where(item => item.Platform == platform).ToArray() : Targets;
    private IReadOnlyList<PortableSetupTarget> ContentTargets => Definition.EmulatorId switch
    {
        "rpcs3" => CurrentHostTarget is { } host ? [host] : [],
        "shadps4" => CurrentHostTarget is { } host ? [host] : SelectedTargets.Take(1).ToArray(),
        _ => SelectedTargets
    };

    public async Task RefreshAsync(CancellationToken token = default)
    {
        var selectedPlatform = _selectedPlatform?.Platform;
        Targets = await service.InspectAsync(token);
        var choices = Targets.Select(item => new PortablePlatformChoice(item.Platform, item.Platform + " x64")).ToList();
        if (Targets.Count > 1) choices.Add(new(null, "Both platforms"));
        PlatformChoices = choices;
        _selectedPlatform = choices.FirstOrDefault(item => item.Platform == selectedPlatform)
            ?? choices.FirstOrDefault(item => item.Platform is null) ?? choices.FirstOrDefault();
        if (_selectedPreset is null)
            _selectedPreset = await PreferredPresetAsync(token);
        await RefreshPreviewsAsync(token);
        Notify();
    }
    public void SetExternalAppRunning(bool running)
    {
        _isExternalAppRunning = running;
        Notify();
    }

    public async Task PreviewAsync(CancellationToken token = default)
    {
        if (SelectedPreset is null || SelectedTargets.Count == 0) return;
        var previews = new List<PortableSetupPreview>();
        foreach (var target in SelectedTargets) previews.Add(await service.PreviewAsync(target, SelectedPreset, token));
        _previewCanApply = previews.Count > 0 && previews.All(item => item.CanApply);
        Changes = previews.SelectMany(preview => preview.Changes.Select(change =>
            SelectedTargets.Count > 1 ? $"{preview.Target.Platform}: {change}" : change)).ToArray();
        Message = previews.All(item => item.CanApply)
            ? previews.All(item => item.Changes.Count == 0)
                ? string.Join(Environment.NewLine, previews.Select(item => item.Guidance).Distinct(StringComparer.Ordinal))
                : "Review the automatic setup below, then apply it."
            : previews.Count == 1 ? previews[0].Guidance : string.Join(Environment.NewLine,
                previews.Where(item => !item.CanApply).Select(item => $"{item.Target.Platform}: {item.Guidance}"));
        Notify();
    }

    private async Task RefreshPreviewsAsync(CancellationToken token = default)
    {
        await PreviewAsync(token);
        await PreviewContentAsync(token);
    }

    public async Task PreviewContentAsync(CancellationToken token = default)
    {
        if (!SupportsContentImport || SelectedTargets.Count == 0) return;
        var targets = ContentTargets;
        if (targets.Count == 0)
        {
            ContentItems = [];
            CanImportContent = false;
            ContentMessage = "Select the platform that can run on this computer to import RPCS3 packages.";
            Notify();
            return;
        }
        var previews = new List<(PortableSetupTarget Target, GameContentImportPreview Preview)>();
        foreach (var target in targets)
            previews.Add((target, await _contentImport.PreviewAsync(target.Platform, token)));
        ContentItems = previews.SelectMany(result =>
            result.Preview.Problems.Select(problem => $"Needs attention: {problem}")
                .Concat(result.Preview.Items.Select(item =>
                    targets.Count > 1 ? $"{result.Target.Platform}: {item.DisplayName}" : item.DisplayName))).ToArray();
        CanImportContent = !_isContentBusy && previews.Count > 0 &&
            previews.All(result => result.Preview.Problems.Count == 0) &&
            previews.Any(result => result.Preview.Items.Count > 0) && Changes.Count == 0;
        ContentMessage = previews.Select(result => result.Preview.Guidance).Distinct(StringComparer.Ordinal).Count() == 1
            ? previews[0].Preview.Guidance
            : string.Join(Environment.NewLine, previews.Select(result => $"{result.Target.Platform}: {result.Preview.Guidance}"));
        if (Changes.Count > 0 && previews.Any(result => result.Preview.Items.Count > 0))
            ContentMessage = "Apply the emulator setup above first. Then import the detected updates and DLC.";
        Notify();
    }

    public async Task ImportContentAsync(CancellationToken token = default)
    {
        if (!CanImportContent) return;
        var targets = ContentTargets;
        _isContentBusy = true;
        CanImportContent = false;
        Notify();
        var succeeded = false;
        try
        {
            var progress = new Progress<string>(message => { ContentMessage = message; Notify(); });
            foreach (var target in targets)
                await _contentImport.ImportAsync(target, progress, token);
            succeeded = true;
        }
        finally
        {
            _isContentBusy = false;
            await PreviewContentAsync(token);
            if (succeeded)
            {
                ContentMessage = SelectedTargets.Count > 1
                    ? "Updates and DLC imported for both platforms."
                    : "Updates and DLC imported successfully.";
                Notify();
            }
        }
    }

    public async Task ApplyAsync(CancellationToken token = default)
    {
        if (!CanApply) return;
        Message = SelectedTargets.Count > 1
            ? $"Preparing {Definition.DisplayName} for both platforms…"
            : $"Applying {Definition.DisplayName} setup…";
        Notify();
        foreach (var target in SelectedTargets) await service.ApplyAsync(target, SelectedPreset!, token);
        await _preferences.SaveSelectedPresetAsync(SelectedPreset!.Id, token);
        Message = SelectedTargets.Count > 1 ? "Portable setup applied to Windows and Linux." : "Portable setup applied.";
        await PreviewAsync(token);
        await PreviewContentAsync(token);
    }
    private async Task<SimpleEmulationPreset> PreferredPresetAsync(CancellationToken token)
    {
        var saved = await _preferences.LoadAsync(token);
        if (saved.SelectedPresetId is { } id) return SimpleEmulationPresetCatalog.Get(id);
        if (SelectedTargets.Count == 0) return SimpleEmulationPresetCatalog.Get("balanced");
        var ranked = new List<(SimpleEmulationPreset Preset, int Changes)>();
        foreach (var preset in Presets)
        {
            var count = 0;
            foreach (var target in SelectedTargets)
                count += (await service.PreviewAsync(target, preset, token)).Changes.Count;
            ranked.Add((preset, count));
        }
        return ranked.OrderBy(item => item.Changes)
            .ThenBy(item => item.Preset.Id == "balanced" ? 0 : 1)
            .First().Preset;
    }
    public void Report(string message) { Message = message; Notify(); }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
