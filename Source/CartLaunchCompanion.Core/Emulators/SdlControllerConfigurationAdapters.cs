using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Writes a verified SDL player layout while preserving settings CLC does not own.</summary>
public abstract class SdlControllerConfigurationAdapter : IEmulatorControllerConfigurationAdapter
{
    private const int MaximumConfigurationBytes = 4 * 1024 * 1024;
    private readonly string _mediaRoot;
    private readonly string _stateRoot;
    private readonly IEmulatorRegistryStore _registry;

    public abstract string EmulatorId { get; }
    protected abstract string ControllerType { get; }
    protected abstract string DisplayName { get; }

    protected SdlControllerConfigurationAdapter(string mediaRoot, string stateRoot)
    {
        _mediaRoot = Path.GetFullPath(mediaRoot);
        _stateRoot = Path.GetFullPath(stateRoot);
        _registry = new EmulatorRegistryStore(stateRoot);
    }

    public async Task<ControllerConfigurationPlan> PreviewAsync(PlatformKind platform,
        ControllerAutoConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePlatform(platform);
        if (request.PlayerNumber is not (1 or 2)) throw new NotSupportedException("Automatic controller setup supports players one and two.");
        var inputSource = $"SDL-{request.PlayerNumber - 1}/";
        if (request.Bindings.Count is <= 0 or > 64 ||
            request.Bindings.GroupBy(binding => binding.Input).Any(group => group.Count() != 1) ||
            request.Bindings.Any(binding => string.IsNullOrWhiteSpace(binding.SdlBinding) ||
                !binding.SdlBinding.StartsWith(inputSource, StringComparison.Ordinal) ||
                binding.SdlBinding.ContainsAny(['\r', '\n'])))
            throw new InvalidDataException("The SDL controller bindings are invalid.");
        var missing = RequiredInputs.Where(input => request.Bindings.All(binding => binding.Input != input)).ToArray();
        if (!request.Controller.HasStandardSdlMapping || missing.Length > 0)
        {
            var reason = !request.Controller.HasStandardSdlMapping
                ? "The controller does not have a standard SDL mapping."
                : $"The verified profile is missing {missing.Length} required inputs.";
            return new(EmulatorId, platform, request, ControllerMappingReadiness.NeedsManualSetup, [], [reason]);
        }

        var path = await ConfigurationPathAsync(platform, cancellationToken);
        var document = await IniFile.LoadAsync(path, cancellationToken);
        var values = Translate(request);
        var changes = values.Where(value => !string.Equals(document.Get(value.Section, value.Key), value.Value,
                StringComparison.Ordinal))
            .ToArray();
        return new(EmulatorId, platform, request, ControllerMappingReadiness.ReadyToApply, changes, []);
    }

    public async Task ApplyAsync(ControllerConfigurationPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.EmulatorId != EmulatorId || plan.Readiness != ControllerMappingReadiness.ReadyToApply)
            throw new InvalidDataException($"The {DisplayName} controller plan is not ready to apply.");
        ValidatePlatform(plan.Platform);
        plan = await PreviewAsync(plan.Platform, plan.Request, cancellationToken);
        if (plan.Readiness != ControllerMappingReadiness.ReadyToApply)
            throw new InvalidDataException($"The {DisplayName} controller plan is no longer ready to apply.");
        EnsureNotRunning();
        var path = await ConfigurationPathAsync(plan.Platform, cancellationToken);
        var document = await IniFile.LoadAsync(path, cancellationToken);
        if (plan.NativeChanges.Count == 0) return;

        if (File.Exists(path))
        {
            var folder = EmulatorPathContract.Resolve(_stateRoot,
                $"Config/EmulatorCompanion/Backups/{EmulatorId}/{plan.Platform}");
            Directory.CreateDirectory(folder);
            var backup = EmulatorPathContract.Resolve(folder,
                $"controller-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.ini");
            File.Copy(path, backup);
            foreach (var old in new DirectoryInfo(folder).EnumerateFiles("controller-*.ini")
                         .OrderByDescending(file => file.CreationTimeUtc).Skip(10)) old.Delete();
        }

        foreach (var value in plan.NativeChanges) document.Set(value.Section, value.Key, value.Value);
        EnsureNotRunning();
        await document.SaveAsync(cancellationToken);
    }

    private IReadOnlyList<EmulatorConfigurationValue> Translate(ControllerAutoConfigurationRequest request)
    {
        var bindings = request.Bindings.ToDictionary(binding => binding.Input, binding => binding.SdlBinding);
        string Get(CanonicalControllerInput input) => bindings[input];
        var south = CanonicalControllerInput.South;
        var east = CanonicalControllerInput.East;
        var west = CanonicalControllerInput.West;
        var north = CanonicalControllerInput.North;
        if (request.Controller.Layout == ControllerLayoutKind.Nintendo &&
            request.FaceButtonPreference == ControllerFaceButtonPreference.PrintedLabels)
            (south, east, west, north) = (east, south, north, west);

        var pad = $"Pad{request.PlayerNumber}";
        var inputSource = $"SDL-{request.PlayerNumber - 1}/";
        var values = new List<EmulatorConfigurationValue>
        {
            new("InputSources", "SDL", "true", "Enable direct SDL controllers"),
            new(pad, "Type", ControllerType, $"Player {request.PlayerNumber} controller type"),
            new(pad, "Up", Get(CanonicalControllerInput.DpadUp), "D-pad up"),
            new(pad, "Down", Get(CanonicalControllerInput.DpadDown), "D-pad down"),
            new(pad, "Left", Get(CanonicalControllerInput.DpadLeft), "D-pad left"),
            new(pad, "Right", Get(CanonicalControllerInput.DpadRight), "D-pad right"),
            new(pad, "Cross", Get(south), "Cross"), new(pad, "Circle", Get(east), "Circle"),
            new(pad, "Square", Get(west), "Square"), new(pad, "Triangle", Get(north), "Triangle"),
            new(pad, "Select", Get(CanonicalControllerInput.Back), "Select"), new(pad, "Start", Get(CanonicalControllerInput.Start), "Start"),
            new(pad, "L1", Get(CanonicalControllerInput.LeftShoulder), "L1"), new(pad, "R1", Get(CanonicalControllerInput.RightShoulder), "R1"),
            new(pad, "L2", Get(CanonicalControllerInput.LeftTrigger), "L2"), new(pad, "R2", Get(CanonicalControllerInput.RightTrigger), "R2"),
            new(pad, "L3", Get(CanonicalControllerInput.LeftStickClick), "L3"), new(pad, "R3", Get(CanonicalControllerInput.RightStickClick), "R3"),
            new(pad, "LLeft", Direction(Get(CanonicalControllerInput.LeftStickX), '-'), "Left stick left"),
            new(pad, "LRight", Direction(Get(CanonicalControllerInput.LeftStickX), '+'), "Left stick right"),
            new(pad, "LUp", Direction(Get(CanonicalControllerInput.LeftStickY), '-'), "Left stick up"),
            new(pad, "LDown", Direction(Get(CanonicalControllerInput.LeftStickY), '+'), "Left stick down"),
            new(pad, "RLeft", Direction(Get(CanonicalControllerInput.RightStickX), '-'), "Right stick left"),
            new(pad, "RRight", Direction(Get(CanonicalControllerInput.RightStickX), '+'), "Right stick right"),
            new(pad, "RUp", Direction(Get(CanonicalControllerInput.RightStickY), '-'), "Right stick up"),
            new(pad, "RDown", Direction(Get(CanonicalControllerInput.RightStickY), '+'), "Right stick down")
        };
        if (request.Controller.SupportsRumble)
        {
            values.Add(new(pad, "LargeMotor", inputSource + "LargeMotor", "Large rumble motor"));
            values.Add(new(pad, "SmallMotor", inputSource + "SmallMotor", "Small rumble motor"));
        }
        return values;
    }

    private async Task<string> ConfigurationPathAsync(PlatformKind platform, CancellationToken token)
    {
        var installation = (await _registry.LoadAsync(token)).Installations.SingleOrDefault(item =>
            item.EmulatorId == EmulatorId && item.Platform == platform)
            ?? throw new IOException($"{DisplayName} is not installed for {platform}.");
        var executable = EmulatorPathContract.Resolve(_mediaRoot, installation.ExecutableRelativePath);
        if (!File.Exists(executable)) throw new IOException($"The recorded {DisplayName} executable is missing.");
        var folder = Path.GetDirectoryName(executable)!;
        return (EmulatorId, platform) switch
        {
            ("pcsx2", PlatformKind.Windows) => EmulatorPathContract.Resolve(folder, "inis/PCSX2.ini"),
            ("pcsx2", PlatformKind.Linux) => EmulatorPathContract.Resolve(executable + ".config", "PCSX2/inis/PCSX2.ini"),
            ("duckstation", PlatformKind.Windows) => EmulatorPathContract.Resolve(folder, "settings.ini"),
            ("duckstation", PlatformKind.Linux) => EmulatorPathContract.Resolve(folder, "settings.ini"),
            _ => throw new NotSupportedException($"{DisplayName} controller setup is unavailable for {platform}.")
        };
    }

    private void EnsureNotRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matched = false;
                try
                {
                    if (!process.ProcessName.StartsWith(EmulatorId, StringComparison.OrdinalIgnoreCase)) continue;
                    matched = true;
                    throw new IOException($"Close {DisplayName} before applying its controller setup.");
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error) when (matched)
                { throw new IOException($"Close {DisplayName} before applying its controller setup.", error); }
            }
        }
    }

    private static string Direction(string binding, char direction)
    {
        var slash = binding.LastIndexOf('/');
        if (slash < 0 || slash == binding.Length - 1) throw new InvalidDataException("An SDL axis binding is invalid.");
        var control = binding[(slash + 1)..].TrimStart('+', '-');
        return binding[..(slash + 1)] + direction + control;
    }

    private static void ValidatePlatform(PlatformKind platform)
    {
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux))
            throw new NotSupportedException("Controller setup supports Windows and Linux only.");
    }

    private static readonly CanonicalControllerInput[] RequiredInputs =
    [
        CanonicalControllerInput.South, CanonicalControllerInput.East, CanonicalControllerInput.West, CanonicalControllerInput.North,
        CanonicalControllerInput.DpadUp, CanonicalControllerInput.DpadDown, CanonicalControllerInput.DpadLeft, CanonicalControllerInput.DpadRight,
        CanonicalControllerInput.LeftShoulder, CanonicalControllerInput.RightShoulder,
        CanonicalControllerInput.LeftTrigger, CanonicalControllerInput.RightTrigger,
        CanonicalControllerInput.LeftStickX, CanonicalControllerInput.LeftStickY,
        CanonicalControllerInput.RightStickX, CanonicalControllerInput.RightStickY,
        CanonicalControllerInput.LeftStickClick, CanonicalControllerInput.RightStickClick,
        CanonicalControllerInput.Start, CanonicalControllerInput.Back
    ];

    private sealed class IniFile
    {
        private readonly string _path;
        private readonly List<string> _lines;
        private IniFile(string path, List<string> lines) { _path = path; _lines = lines; }
        public static async Task<IniFile> LoadAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new(path, []);
            if (new FileInfo(path).Length > MaximumConfigurationBytes)
                throw new InvalidDataException($"The configuration is too large to edit safely.");
            var text = await File.ReadAllTextAsync(path, token);
            return new(path, text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList());
        }
        public string? Get(string section, string key)
        {
            var range = Range(section); if (range is null) return null;
            for (var index = range.Value.Start + 1; index < range.Value.End; index++)
                if (TryKey(_lines[index], out var found, out var value) && found.Equals(key, StringComparison.OrdinalIgnoreCase)) return value;
            return null;
        }
        public void Set(string section, string key, string value)
        {
            var range = Range(section);
            if (range is null)
            {
                if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
                _lines.Add($"[{section}]"); _lines.Add($"{key} = {value}"); return;
            }
            for (var index = range.Value.Start + 1; index < range.Value.End; index++)
                if (TryKey(_lines[index], out var found, out _) && found.Equals(key, StringComparison.OrdinalIgnoreCase))
                { _lines[index] = $"{key} = {value}"; return; }
            _lines.Insert(range.Value.End, $"{key} = {value}");
        }
        public async Task SaveAsync(CancellationToken token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, string.Join(Environment.NewLine, _lines).TrimEnd() + Environment.NewLine,
                    new UTF8Encoding(false), token);
                File.Move(temporary, _path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private (int Start, int End)? Range(string section)
        {
            var start = -1;
            for (var index = 0; index < _lines.Count; index++)
            {
                var line = _lines[index].Trim(); if (!line.StartsWith('[') || !line.EndsWith(']')) continue;
                if (start >= 0) return (start, index);
                if (line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase)) start = index;
            }
            return start < 0 ? null : (start, _lines.Count);
        }
        private static bool TryKey(string line, out string key, out string value)
        {
            key = value = ""; var equals = line.IndexOf('='); var trimmed = line.TrimStart();
            if (equals <= 0 || trimmed.Length == 0 || trimmed[0] is ';' or '#') return false;
            key = line[..equals].Trim(); value = line[(equals + 1)..].Trim(); return key.Length > 0;
        }
    }
}

public sealed class Pcsx2ControllerConfigurationAdapter(string mediaRoot, string stateRoot)
    : SdlControllerConfigurationAdapter(mediaRoot, stateRoot)
{
    public override string EmulatorId => "pcsx2";
    protected override string ControllerType => "DualShock2";
    protected override string DisplayName => "PCSX2";
}

public sealed class DuckStationControllerConfigurationAdapter(string mediaRoot, string stateRoot)
    : SdlControllerConfigurationAdapter(mediaRoot, stateRoot)
{
    public override string EmulatorId => "duckstation";
    protected override string ControllerType => "AnalogController";
    protected override string DisplayName => "DuckStation";
}
