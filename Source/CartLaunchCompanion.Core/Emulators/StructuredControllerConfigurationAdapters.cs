using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Writes RPCS3's native SDL YAML profile from the verified library layout.</summary>
public sealed class Rpcs3ControllerConfigurationAdapter : IEmulatorControllerConfigurationAdapter
{
    private readonly NativeControllerConfigurationFiles _files;
    public string EmulatorId => "rpcs3";
    public Rpcs3ControllerConfigurationAdapter(string mediaRoot, string stateRoot) =>
        _files = new(mediaRoot, stateRoot, EmulatorId, "RPCS3");

    public async Task<ControllerConfigurationPlan> PreviewAsync(PlatformKind platform,
        ControllerAutoConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        NativeControllerConfigurationFiles.Validate(platform, request);
        if (!request.Controller.HasStandardSdlMapping)
            return new(EmulatorId, platform, request, ControllerMappingReadiness.NeedsManualSetup, [],
                ["The controller does not have a standard SDL mapping."]);
        var path = await _files.PathAsync(platform, (executable, folder) => platform == PlatformKind.Windows
            ? EmulatorPathContract.Resolve(folder, "config/input_configs/global/Default.yml")
            : EmulatorPathContract.Resolve(executable + ".config", "rpcs3/input_configs/global/Default.yml"), cancellationToken);
        var current = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : "";
        var block = PlayerBlock(request);
        var changes = ContainsBlock(current, block) ? Array.Empty<EmulatorConfigurationValue>() :
            [new EmulatorConfigurationValue($"Player {request.PlayerNumber} Input", "Profile", "SDL", "Translate the library controller for RPCS3")];
        return new(EmulatorId, platform, request, ControllerMappingReadiness.ReadyToApply, changes, []);
    }

    public async Task ApplyAsync(ControllerConfigurationPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.EmulatorId != EmulatorId || plan.Readiness != ControllerMappingReadiness.ReadyToApply)
            throw new InvalidDataException("The RPCS3 controller plan is not ready to apply.");
        plan = await PreviewAsync(plan.Platform, plan.Request, cancellationToken);
        if (plan.NativeChanges.Count == 0) return;
        _files.EnsureNotRunning();
        var path = await _files.PathAsync(plan.Platform, (executable, folder) => plan.Platform == PlatformKind.Windows
            ? EmulatorPathContract.Resolve(folder, "config/input_configs/global/Default.yml")
            : EmulatorPathContract.Resolve(executable + ".config", "rpcs3/input_configs/global/Default.yml"), cancellationToken);
        var current = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : "";
        _files.Backup(path, plan.Platform, ".yml");
        var heading = $"Player {plan.Request.PlayerNumber} Input:";
        var updated = ReplaceTopLevelBlock(current, heading, PlayerBlock(plan.Request));
        await NativeControllerConfigurationFiles.AtomicWriteAsync(path, updated, cancellationToken);
    }

    private static string PlayerBlock(ControllerAutoConfigurationRequest request)
    {
        var (south, east, west, north) = FaceButtons(request);
        var device = Quote(request.Controller.DisplayName + " " + request.Controller.DeviceOrdinal);
        return string.Join('\n',
        [
            $"Player {request.PlayerNumber} Input:", "  Handler: SDL", $"  Device: {device}", "  Config:",
            "    Left Stick Left: \"LS X-\"", "    Left Stick Down: \"LS Y-\"",
            "    Left Stick Right: \"LS X+\"", "    Left Stick Up: \"LS Y+\"",
            "    Right Stick Left: \"RS X-\"", "    Right Stick Down: \"RS Y-\"",
            "    Right Stick Right: \"RS X+\"", "    Right Stick Up: \"RS Y+\"",
            "    Start: Start", "    Select: Back", "    PS Button: \"Guide,Start&Back\"",
            $"    Square: {west}", $"    Cross: {south}", $"    Circle: {east}", $"    Triangle: {north}",
            "    Left: Left", "    Down: Down", "    Right: Right", "    Up: Up",
            "    R1: RB", "    R2: RT", "    R3: RS", "    L1: LB", "    L2: LT", "    L3: LS"
        ]) + "\n";
    }

    private static (string South, string East, string West, string North) FaceButtons(
        ControllerAutoConfigurationRequest request) =>
        request.Controller.Layout == ControllerLayoutKind.Nintendo &&
        request.FaceButtonPreference == ControllerFaceButtonPreference.PrintedLabels
            ? ("East", "South", "North", "West") : ("South", "East", "West", "North");

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal) + "\"";
    private static bool ContainsBlock(string current, string block) =>
        Normalize(current).Contains(Normalize(block), StringComparison.Ordinal);
    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    private static string ReplaceTopLevelBlock(string current, string heading, string replacement)
    {
        var lines = current.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        var start = lines.FindIndex(line => line.Equals(heading, StringComparison.Ordinal));
        if (start >= 0)
        {
            var end = start + 1;
            while (end < lines.Count && (lines[end].Length == 0 || char.IsWhiteSpace(lines[end][0]))) end++;
            lines.RemoveRange(start, end - start);
        }
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > 0) lines.Add("");
        lines.AddRange(replacement.TrimEnd().Split('\n'));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

/// <summary>Enables shadPS4's unified SDL input path for the verified controller.</summary>
public sealed class ShadPs4ControllerConfigurationAdapter : IEmulatorControllerConfigurationAdapter
{
    private readonly NativeControllerConfigurationFiles _files;
    public string EmulatorId => "shadps4";
    public ShadPs4ControllerConfigurationAdapter(string mediaRoot, string stateRoot) =>
        _files = new(mediaRoot, stateRoot, EmulatorId, "shadPS4");

    public async Task<ControllerConfigurationPlan> PreviewAsync(PlatformKind platform,
        ControllerAutoConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        NativeControllerConfigurationFiles.Validate(platform, request);
        if (!request.Controller.HasStandardSdlMapping)
            return new(EmulatorId, platform, request, ControllerMappingReadiness.NeedsManualSetup, [],
                ["The controller does not have a standard SDL mapping."]);
        var path = await ConfigPathAsync(platform, cancellationToken);
        var root = await ReadAsync(path, cancellationToken);
        var input = root["Input"] as JsonObject;
        var changes = input?["use_unified_input_config"]?.GetValue<bool>() == true
            ? Array.Empty<EmulatorConfigurationValue>()
            : [new EmulatorConfigurationValue("Input", "use_unified_input_config", "true", "Use the verified SDL layout")];
        return new(EmulatorId, platform, request, ControllerMappingReadiness.ReadyToApply, changes, []);
    }

    public async Task ApplyAsync(ControllerConfigurationPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.EmulatorId != EmulatorId || plan.Readiness != ControllerMappingReadiness.ReadyToApply)
            throw new InvalidDataException("The shadPS4 controller plan is not ready to apply.");
        plan = await PreviewAsync(plan.Platform, plan.Request, cancellationToken);
        if (plan.NativeChanges.Count == 0) return;
        _files.EnsureNotRunning();
        var path = await ConfigPathAsync(plan.Platform, cancellationToken);
        var root = await ReadAsync(path, cancellationToken);
        var input = root["Input"] as JsonObject ?? new JsonObject();
        root["Input"] = input;
        input["use_unified_input_config"] = true;
        _files.Backup(path, plan.Platform, ".json");
        await NativeControllerConfigurationFiles.AtomicWriteAsync(path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, cancellationToken);
    }

    private Task<string> ConfigPathAsync(PlatformKind platform, CancellationToken token) =>
        _files.PathAsync(platform, (_, folder) => EmulatorPathContract.Resolve(folder, "user/config.json"), token);
    private static async Task<JsonObject> ReadAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The shadPS4 configuration is too large.");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, token)) as JsonObject
            ?? throw new InvalidDataException("The shadPS4 configuration is unreadable.");
    }
}

internal sealed class NativeControllerConfigurationFiles
{
    private readonly string _mediaRoot;
    private readonly string _stateRoot;
    private readonly string _emulatorId;
    private readonly string _displayName;
    private readonly IEmulatorRegistryStore _registry;
    public NativeControllerConfigurationFiles(string mediaRoot, string stateRoot, string emulatorId, string displayName)
    {
        _mediaRoot = Path.GetFullPath(mediaRoot); _stateRoot = Path.GetFullPath(stateRoot);
        _emulatorId = emulatorId; _displayName = displayName; _registry = new EmulatorRegistryStore(stateRoot);
    }
    public async Task<string> PathAsync(PlatformKind platform, Func<string, string, string> select,
        CancellationToken token)
    {
        var installation = (await _registry.LoadAsync(token)).Installations.SingleOrDefault(item =>
            item.EmulatorId == _emulatorId && item.Platform == platform)
            ?? throw new IOException($"{_displayName} is not installed for {platform}.");
        var executable = EmulatorPathContract.Resolve(_mediaRoot, installation.ExecutableRelativePath);
        if (!File.Exists(executable)) throw new IOException($"The recorded {_displayName} executable is missing.");
        return select(executable, Path.GetDirectoryName(executable)!);
    }
    public void Backup(string path, PlatformKind platform, string extension)
    {
        if (!File.Exists(path)) return;
        var folder = EmulatorPathContract.Resolve(_stateRoot,
            $"Config/EmulatorCompanion/Backups/{_emulatorId}/{platform}");
        Directory.CreateDirectory(folder);
        File.Copy(path, EmulatorPathContract.Resolve(folder,
            $"controller-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}"));
        foreach (var old in new DirectoryInfo(folder).EnumerateFiles("controller-*" + extension)
                     .OrderByDescending(file => file.CreationTimeUtc).Skip(10)) old.Delete();
    }
    public void EnsureNotRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matched = false;
                try
                {
                    if (!process.ProcessName.StartsWith(_emulatorId, StringComparison.OrdinalIgnoreCase)) continue;
                    matched = true; throw new IOException($"Close {_displayName} before applying its controller setup.");
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error) when (matched)
                { throw new IOException($"Close {_displayName} before applying its controller setup.", error); }
            }
        }
    }
    public static void Validate(PlatformKind platform, ControllerAutoConfigurationRequest request)
    {
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux))
            throw new NotSupportedException("Controller setup supports Windows and Linux only.");
        if (request.PlayerNumber is not (1 or 2) || request.Bindings.Count == 0)
            throw new InvalidDataException("The library controller profile is invalid.");
    }
    public static async Task AtomicWriteAsync(string path, string content, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
