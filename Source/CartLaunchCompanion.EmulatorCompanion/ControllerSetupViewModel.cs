using System.ComponentModel;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.EmulatorCompanion.Input;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed record FaceButtonChoice(ControllerFaceButtonPreference Value, string DisplayName, string Description);
public sealed record ControllerPlayerChoice(int PlayerNumber, string DisplayName, string Description);

public sealed class ControllerSetupViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly IReadOnlyList<CanonicalControllerInput> Required =
    [
        CanonicalControllerInput.DpadUp, CanonicalControllerInput.DpadDown, CanonicalControllerInput.DpadLeft, CanonicalControllerInput.DpadRight,
        CanonicalControllerInput.South, CanonicalControllerInput.East, CanonicalControllerInput.West, CanonicalControllerInput.North,
        CanonicalControllerInput.LeftShoulder, CanonicalControllerInput.RightShoulder,
        CanonicalControllerInput.LeftTrigger, CanonicalControllerInput.RightTrigger,
        CanonicalControllerInput.LeftStickX, CanonicalControllerInput.LeftStickY,
        CanonicalControllerInput.RightStickX, CanonicalControllerInput.RightStickY,
        CanonicalControllerInput.LeftStickClick, CanonicalControllerInput.RightStickClick,
        CanonicalControllerInput.Start, CanonicalControllerInput.Back
    ];
    private readonly PlatformKind _platform;
    private readonly VerifiedControllerProfileStore _store;
    private readonly IReadOnlyList<IEmulatorControllerConfigurationAdapter> _adapters;
    private readonly IReadOnlyList<string> _nativeAutoDetection;
    private readonly SdlControllerProbe _probe;
    private readonly HashSet<CanonicalControllerInput> _verified = [];
    private FaceButtonChoice _selectedFaceButtons;
    private ControllerPlayerChoice _selectedPlayer;
    private bool _testing;

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<FaceButtonChoice> FaceButtonChoices { get; } =
    [
        new(ControllerFaceButtonPreference.PhysicalPosition, "Physical position", "Recommended. The same physical button performs the same action on every controller."),
        new(ControllerFaceButtonPreference.PrintedLabels, "Printed labels", "Match A, B, X, and Y printed on the controller, including Nintendo layouts.")
    ];
    public IReadOnlyList<ControllerPlayerChoice> PlayerChoices { get; } =
    [
        new(1, "Player 1", "Primary controller used for single-player games."),
        new(2, "Player 2", "Optional second local controller for supported multiplayer games.")
    ];
    public ControllerPlayerChoice SelectedPlayer
    {
        get => _selectedPlayer;
        set
        {
            if (_selectedPlayer == value) return;
            _selectedPlayer = value;
            _verified.Clear(); _testing = false;
            _probe.SelectControllerIndex(value.PlayerNumber - 1);
            Message = $"Waiting for the {value.DisplayName} controller…";
            Notify();
        }
    }
    public FaceButtonChoice SelectedFaceButtons { get => _selectedFaceButtons; set { _selectedFaceButtons = value; Notify(); } }
    public DetectedGameController? Controller { get; private set; }
    public string ControllerName => Controller?.DisplayName ?? "No controller detected";
    public string ControllerDetails => Controller is null ? "Connect a controller directly by USB or Bluetooth." :
        $"{Controller.Family.DisplayName()} · {Controller.Layout} layout · " +
        (Controller.Mapping == ControllerMappingKind.BuiltInXboxFallback ? "Xbox-style fallback; input test required" :
            Controller.HasStandardSdlMapping ? "standard SDL mapping" : "manual mapping required");
    public bool HasController => Controller is not null;
    public bool CanStart => Controller?.HasStandardSdlMapping == true;
    public bool CanSave => _testing && _verified.Count == Required.Count;
    public bool IsTesting => _testing;
    public string SaveActionLabel => $"Save {SelectedPlayer.DisplayName} and apply";
    public string SetupScope
    {
        get
        {
            var translated = _adapters.Select(AdapterName).ToArray();
            var parts = new List<string>();
            if (translated.Length > 0) parts.Add("translate settings for " + string.Join(" and ", translated));
            if (_nativeAutoDetection.Count > 0) parts.Add(string.Join(" and ", _nativeAutoDetection) + " will use the verified device through native automatic detection");
            return parts.Count == 0
                ? $"This {SelectedPlayer.DisplayName} {_platform} profile will be used when compatible emulators are installed."
                : $"{SelectedPlayer.DisplayName}: one test will " + string.Join("; ", parts) + ".";
        }
    }
    public int CompletedCount => _verified.Count;
    public int TotalCount => Required.Count;
    public double Progress => Required.Count == 0 ? 0 : (double)_verified.Count / Required.Count * 100;
    public string ProgressText => $"{CompletedCount} of {TotalCount} inputs verified";
    public string Prompt => !_testing ? "Select Start input test when you are ready." : CanSave ?
        "Controller test complete. Save this verified profile." : "Next: " + Label(Required.First(input => !_verified.Contains(input)));
    public IReadOnlyList<string> VerifiedLabels => Required.Where(_verified.Contains).Select(input => "✓ " + Label(input)).ToArray();
    public string Message { get; private set; } = "Waiting for a controller…";

    public ControllerSetupViewModel(PlatformKind platform, string mediaRoot, string stateRoot,
        IEnumerable<string> installedEmulatorIds, SdlControllerProbe? probe = null)
    {
        _platform = platform; _store = new(stateRoot); _probe = probe ?? new();
        var ids = installedEmulatorIds.ToHashSet(StringComparer.Ordinal);
        _adapters = ids.Select(id => ControllerConfigurationAdapterCatalog.Create(id, mediaRoot, stateRoot))
            .Where(adapter => adapter is not null)
            .Cast<IEmulatorControllerConfigurationAdapter>()
            .ToArray();
        _nativeAutoDetection = [];
        _selectedPlayer = PlayerChoices[0];
        _selectedFaceButtons = FaceButtonChoices[0];
        _probe.ControllerChanged += ControllerChanged; _probe.InputActivated += InputActivated;
        _probe.DiagnosticChanged += (_, message) => { Message = message; Notify(); };
    }
    public void Start() => _probe.Start();
    public void StartTest()
    {
        if (!CanStart) return; _verified.Clear(); _testing = true;
        Message = "Move each control when prompted. Inputs may be completed in any order."; Notify();
    }
    public async Task SaveAsync(CancellationToken token = default)
    {
        if (!CanSave || Controller is null) return;
        var playerNumber = SelectedPlayer.PlayerNumber;
        var bindings = Bindings(playerNumber);
        var profile = new VerifiedControllerProfile(VerifiedControllerProfileStore.CurrentSchemaVersion,
            VerifiedControllerProfileStore.SharedProfileIdForPlayer(playerNumber), _platform, Controller,
            SelectedFaceButtons.Value, bindings, Required, DateTimeOffset.UtcNow, playerNumber);
        var request = new ControllerAutoConfigurationRequest(Controller, playerNumber, SelectedFaceButtons.Value, bindings);
        var plans = new List<(IEmulatorControllerConfigurationAdapter Adapter, ControllerConfigurationPlan Plan)>();
        foreach (var adapter in _adapters)
        {
            var plan = await adapter.PreviewAsync(_platform, request, token);
            if (plan.Readiness != ControllerMappingReadiness.ReadyToApply)
                throw new InvalidDataException(string.Join(" ", plan.Warnings));
            plans.Add((adapter, plan));
        }
        foreach (var (adapter, plan) in plans) await adapter.ApplyAsync(plan, token);
        await _store.SaveAsync(profile, token);
        var results = new List<string>();
        if (_adapters.Count > 0) results.Add("applied to " + string.Join(" and ", _adapters.Select(AdapterName)));
        if (_nativeAutoDetection.Count > 0) results.Add("verified for " + string.Join(" and ", _nativeAutoDetection));
        Message = results.Count == 0
            ? $"{SelectedPlayer.DisplayName} saved for future {_platform} emulator setups."
            : $"{SelectedPlayer.DisplayName} saved, " + string.Join("; ", results) + ".";
        Notify();
    }
    public void Report(string message) { Message = message; Notify(); }
    private void ControllerChanged(object? sender, DetectedGameController? controller)
    {
        Controller = controller; _verified.Clear(); _testing = false;
        Message = controller is null ? "Waiting for a controller…" : controller.HasStandardSdlMapping ?
            "Controller recognized. Start the input test." : "SDL does not have a standard mapping for this controller. Use the emulator's manual mapping screen.";
        Notify();
    }
    private void InputActivated(object? sender, CanonicalControllerInput input)
    { if (_testing && Required.Contains(input) && _verified.Add(input)) Notify(); }
    private static IReadOnlyList<CanonicalControllerBinding> Bindings(int playerNumber)
    {
        var prefix = $"SDL-{playerNumber - 1}/";
        return
    [
        new(CanonicalControllerInput.South, prefix + "A"), new(CanonicalControllerInput.East, prefix + "B"),
        new(CanonicalControllerInput.West, prefix + "X"), new(CanonicalControllerInput.North, prefix + "Y"),
        new(CanonicalControllerInput.DpadUp, prefix + "DPadUp"), new(CanonicalControllerInput.DpadDown, prefix + "DPadDown"),
        new(CanonicalControllerInput.DpadLeft, prefix + "DPadLeft"), new(CanonicalControllerInput.DpadRight, prefix + "DPadRight"),
        new(CanonicalControllerInput.LeftShoulder, prefix + "LeftShoulder"), new(CanonicalControllerInput.RightShoulder, prefix + "RightShoulder"),
        new(CanonicalControllerInput.LeftTrigger, prefix + "+LeftTrigger"), new(CanonicalControllerInput.RightTrigger, prefix + "+RightTrigger"),
        new(CanonicalControllerInput.LeftStickX, prefix + "LeftX"), new(CanonicalControllerInput.LeftStickY, prefix + "LeftY"),
        new(CanonicalControllerInput.RightStickX, prefix + "RightX"), new(CanonicalControllerInput.RightStickY, prefix + "RightY"),
        new(CanonicalControllerInput.LeftStickClick, prefix + "LeftStick"), new(CanonicalControllerInput.RightStickClick, prefix + "RightStick"),
        new(CanonicalControllerInput.Start, prefix + "Start"), new(CanonicalControllerInput.Back, prefix + "Back")
    ];
    }
    private static string Label(CanonicalControllerInput input) => input switch
    {
        CanonicalControllerInput.South => "bottom face button (A / Cross)", CanonicalControllerInput.East => "right face button (B / Circle)",
        CanonicalControllerInput.West => "left face button (X / Square)", CanonicalControllerInput.North => "top face button (Y / Triangle)",
        CanonicalControllerInput.LeftStickX => "move the left stick left or right", CanonicalControllerInput.LeftStickY => "move the left stick up or down",
        CanonicalControllerInput.RightStickX => "move the right stick left or right", CanonicalControllerInput.RightStickY => "move the right stick up or down",
        CanonicalControllerInput.LeftStickClick => "press the left stick", CanonicalControllerInput.RightStickClick => "press the right stick",
        CanonicalControllerInput.LeftTrigger => "left trigger", CanonicalControllerInput.RightTrigger => "right trigger",
        CanonicalControllerInput.LeftShoulder => "left shoulder", CanonicalControllerInput.RightShoulder => "right shoulder",
        CanonicalControllerInput.DpadUp => "D-pad up", CanonicalControllerInput.DpadDown => "D-pad down",
        CanonicalControllerInput.DpadLeft => "D-pad left", CanonicalControllerInput.DpadRight => "D-pad right",
        CanonicalControllerInput.Start => "Start / Options", CanonicalControllerInput.Back => "Back / Select / Create", _ => input.ToString()
    };
    private static string AdapterName(IEmulatorControllerConfigurationAdapter adapter) => adapter.EmulatorId switch
    {
        "pcsx2" => "PCSX2", "duckstation" => "DuckStation", "ppsspp" => "PPSSPP",
        "rpcs3" => "RPCS3", "shadps4" => "shadPS4", _ => adapter.EmulatorId
    };
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public async ValueTask DisposeAsync()
    {
        _probe.ControllerChanged -= ControllerChanged; _probe.InputActivated -= InputActivated; await _probe.DisposeAsync();
    }
}
