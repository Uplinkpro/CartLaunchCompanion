using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Creates the native translator used by each managed emulator.</summary>
public static class ControllerConfigurationAdapterCatalog
{
    public static IEmulatorControllerConfigurationAdapter? Create(string emulatorId, string mediaRoot, string stateRoot) =>
        emulatorId switch
        {
            "pcsx2" => new Pcsx2ControllerConfigurationAdapter(mediaRoot, stateRoot),
            "duckstation" => new DuckStationControllerConfigurationAdapter(mediaRoot, stateRoot),
            "ppsspp" => new PpssppControllerConfigurationAdapter(mediaRoot, stateRoot),
            "rpcs3" => new Rpcs3ControllerConfigurationAdapter(mediaRoot, stateRoot),
            "shadps4" => new ShadPs4ControllerConfigurationAdapter(mediaRoot, stateRoot),
            _ => null
        };
}

/// <summary>Reuses the verified library profile when an emulator is installed later.</summary>
public sealed class LibraryControllerProfileApplicator(string mediaRoot, string stateRoot)
{
    private readonly VerifiedControllerProfileStore _profiles = new(stateRoot);

    public async Task<bool> ApplyAsync(string emulatorId, PlatformKind platform,
        CancellationToken cancellationToken = default)
    {
        var adapter = ControllerConfigurationAdapterCatalog.Create(emulatorId, mediaRoot, stateRoot);
        if (adapter is null) return false;
        var applied = false;
        for (var player = 1; player <= 2; player++)
        {
            var profile = await _profiles.LoadAsync(
                VerifiedControllerProfileStore.SharedProfileIdForPlayer(player), platform, cancellationToken);
            if (profile is null) continue;
            var request = new ControllerAutoConfigurationRequest(profile.Controller, player,
                profile.FaceButtonPreference, profile.Bindings);
            var plan = await adapter.PreviewAsync(platform, request, cancellationToken);
            if (plan.Readiness != ControllerMappingReadiness.ReadyToApply)
                throw new InvalidDataException(string.Join(" ", plan.Warnings));
            await adapter.ApplyAsync(plan, cancellationToken);
            applied = true;
        }
        return applied;
    }
}

/// <summary>Translates the canonical library layout into PPSSPP's numeric controls.ini format.</summary>
public sealed class PpssppControllerConfigurationAdapter : IEmulatorControllerConfigurationAdapter
{
    private const int MaximumConfigurationBytes = 4 * 1024 * 1024;
    private readonly string _mediaRoot;
    private readonly string _stateRoot;
    private readonly IEmulatorRegistryStore _registry;
    public string EmulatorId => "ppsspp";

    public PpssppControllerConfigurationAdapter(string mediaRoot, string stateRoot)
    {
        _mediaRoot = Path.GetFullPath(mediaRoot);
        _stateRoot = Path.GetFullPath(stateRoot);
        _registry = new EmulatorRegistryStore(stateRoot);
    }

    public async Task<ControllerConfigurationPlan> PreviewAsync(PlatformKind platform,
        ControllerAutoConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(platform, request);
        if (!request.Controller.HasStandardSdlMapping)
            return new(EmulatorId, platform, request, ControllerMappingReadiness.NeedsManualSetup, [],
                ["The controller does not have a standard SDL mapping."]);
        if (request.PlayerNumber == 2)
            return new(EmulatorId, platform, request, ControllerMappingReadiness.ReadyToApply, [],
                ["PPSSPP emulates a handheld system with one local controller."]);
        var path = await ConfigurationPathAsync(platform, cancellationToken);
        var document = await IniFile.LoadAsync(path, cancellationToken);
        var changes = Translate(request).Where(value =>
            !string.Equals(document.Get(value.Section, value.Key), value.Value, StringComparison.Ordinal)).ToArray();
        return new(EmulatorId, platform, request, ControllerMappingReadiness.ReadyToApply, changes, []);
    }

    public async Task ApplyAsync(ControllerConfigurationPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.EmulatorId != EmulatorId || plan.Readiness != ControllerMappingReadiness.ReadyToApply)
            throw new InvalidDataException("The PPSSPP controller plan is not ready to apply.");
        plan = await PreviewAsync(plan.Platform, plan.Request, cancellationToken);
        if (plan.NativeChanges.Count == 0) return;
        EnsureNotRunning();
        var path = await ConfigurationPathAsync(plan.Platform, cancellationToken);
        var document = await IniFile.LoadAsync(path, cancellationToken);
        Backup(path, plan.Platform);
        foreach (var change in plan.NativeChanges) document.Set(change.Section, change.Key, change.Value);
        EnsureNotRunning();
        await document.SaveAsync(cancellationToken);
    }

    private static IReadOnlyList<EmulatorConfigurationValue> Translate(ControllerAutoConfigurationRequest request)
    {
        var south = CanonicalControllerInput.South;
        var east = CanonicalControllerInput.East;
        var west = CanonicalControllerInput.West;
        var north = CanonicalControllerInput.North;
        if (request.Controller.Layout == ControllerLayoutKind.Nintendo &&
            request.FaceButtonPreference == ControllerFaceButtonPreference.PrintedLabels)
            (south, east, west, north) = (east, south, north, west);
        var buttons = new Dictionary<CanonicalControllerInput, string>
        {
            [CanonicalControllerInput.South] = "10-189", [CanonicalControllerInput.East] = "10-190",
            [CanonicalControllerInput.West] = "10-191", [CanonicalControllerInput.North] = "10-188"
        };
        return
        [
            new("ControlMapping", "Up", "10-19", "D-pad up"),
            new("ControlMapping", "Down", "10-20", "D-pad down"),
            new("ControlMapping", "Left", "10-21", "D-pad left"),
            new("ControlMapping", "Right", "10-22", "D-pad right"),
            new("ControlMapping", "Cross", buttons[south], "Cross"),
            new("ControlMapping", "Circle", buttons[east], "Circle"),
            new("ControlMapping", "Square", buttons[west], "Square"),
            new("ControlMapping", "Triangle", buttons[north], "Triangle"),
            new("ControlMapping", "Start", "10-197", "Start"),
            new("ControlMapping", "Select", "10-196", "Select"),
            new("ControlMapping", "L", "10-193", "L shoulder"),
            new("ControlMapping", "R", "10-192", "R shoulder"),
            new("ControlMapping", "An.Up", "10-4003", "Analog up"),
            new("ControlMapping", "An.Down", "10-4002", "Analog down"),
            new("ControlMapping", "An.Left", "10-4001", "Analog left"),
            new("ControlMapping", "An.Right", "10-4000", "Analog right")
        ];
    }

    private async Task<string> ConfigurationPathAsync(PlatformKind platform, CancellationToken token)
    {
        var installation = (await _registry.LoadAsync(token)).Installations.SingleOrDefault(item =>
            item.EmulatorId == EmulatorId && item.Platform == platform)
            ?? throw new IOException($"PPSSPP is not installed for {platform}.");
        var executable = EmulatorPathContract.Resolve(_mediaRoot, installation.ExecutableRelativePath);
        if (!File.Exists(executable)) throw new IOException("The recorded PPSSPP executable is missing.");
        var folder = Path.GetDirectoryName(executable)!;
        return platform switch
        {
            PlatformKind.Windows => EmulatorPathContract.Resolve(folder, "memstick/PSP/SYSTEM/controls.ini"),
            PlatformKind.Linux => EmulatorPathContract.Resolve(executable + ".config", "ppsspp/PSP/SYSTEM/controls.ini"),
            _ => throw new NotSupportedException("Controller setup supports Windows and Linux only.")
        };
    }

    private static void Validate(PlatformKind platform, ControllerAutoConfigurationRequest request)
    {
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux))
            throw new NotSupportedException("Controller setup supports Windows and Linux only.");
        if (request.PlayerNumber is not (1 or 2) || request.Bindings.Count == 0)
            throw new InvalidDataException("The library controller profile is invalid.");
    }

    private void Backup(string path, PlatformKind platform)
    {
        if (!File.Exists(path)) return;
        var folder = EmulatorPathContract.Resolve(_stateRoot,
            $"Config/EmulatorCompanion/Backups/{EmulatorId}/{platform}");
        Directory.CreateDirectory(folder);
        File.Copy(path, EmulatorPathContract.Resolve(folder,
            $"controller-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.ini"));
        foreach (var old in new DirectoryInfo(folder).EnumerateFiles("controller-*.ini")
                     .OrderByDescending(file => file.CreationTimeUtc).Skip(10)) old.Delete();
    }

    private static void EnsureNotRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matched = false;
                try
                {
                    if (!process.ProcessName.StartsWith("ppsspp", StringComparison.OrdinalIgnoreCase)) continue;
                    matched = true;
                    throw new IOException("Close PPSSPP before applying its controller setup.");
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error) when (matched)
                { throw new IOException("Close PPSSPP before applying its controller setup.", error); }
            }
        }
    }

    private sealed class IniFile
    {
        private readonly string _path;
        private readonly List<string> _lines;
        private IniFile(string path, List<string> lines) { _path = path; _lines = lines; }
        public static async Task<IniFile> LoadAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new(path, []);
            if (new FileInfo(path).Length > MaximumConfigurationBytes)
                throw new InvalidDataException("The PPSSPP controller configuration is too large.");
            var text = await File.ReadAllTextAsync(path, token);
            return new(path, text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList());
        }
        public string? Get(string section, string key)
        {
            var range = Range(section); if (range is null) return null;
            for (var i = range.Value.Start + 1; i < range.Value.End; i++)
                if (TryKey(_lines[i], out var found, out var value) && found.Equals(key, StringComparison.OrdinalIgnoreCase)) return value;
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
            for (var i = range.Value.Start + 1; i < range.Value.End; i++)
                if (TryKey(_lines[i], out var found, out _) && found.Equals(key, StringComparison.OrdinalIgnoreCase))
                { _lines[i] = $"{key} = {value}"; return; }
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
            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i].Trim(); if (!line.StartsWith('[') || !line.EndsWith(']')) continue;
                if (start >= 0) return (start, i);
                if (line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase)) start = i;
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
