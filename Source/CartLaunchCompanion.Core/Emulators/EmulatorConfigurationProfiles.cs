using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Emulators;

public enum EmulatorConfigurationScope { Global, Game }
public enum EmulatorConfigurationValueBehavior { Replace, EnsureListContains }

public sealed record EmulatorConfigurationValue(string Section, string Key, string Value, string DisplayName,
    EmulatorConfigurationValueBehavior Behavior = EmulatorConfigurationValueBehavior.Replace);

public sealed record EmulatorConfigurationProfile(
    int SchemaVersion,
    string EmulatorId,
    string Id,
    string DisplayName,
    string Description,
    EmulatorConfigurationScope Scope,
    IReadOnlyList<EmulatorConfigurationValue> Values);

public sealed record EmulatorConfigurationChange(
    string Section, string Key, string DisplayName, string? PreviousValue, string NewValue)
{
    public string Summary => $"{DisplayName}: {PreviousValue ?? "Not set"} → {NewValue}";
}

public sealed record EmulatorConfigurationPreview(
    EmulatorConfigurationProfile Profile,
    PlatformKind Platform,
    string ConfigurationPath,
    IReadOnlyList<EmulatorConfigurationChange> Changes,
    bool CanRestore);

public sealed record EmulatorConfigurationApplyResult(
    EmulatorConfigurationPreview Preview,
    DateTimeOffset AppliedAt,
    bool BackupCreated);

public interface IEmulatorConfigurationAdapter
{
    string EmulatorId { get; }
    int AdapterVersion { get; }
    IReadOnlyList<SimpleEmulationPreset> Presets { get; }
    EmulatorConfigurationProfile Translate(SimpleEmulationPreset preset);
    Task<EmulatorConfigurationPreview> PreviewAsync(PlatformKind platform, EmulatorConfigurationProfile profile,
        CancellationToken cancellationToken = default);
    Task<EmulatorConfigurationApplyResult> ApplyAsync(PlatformKind platform, EmulatorConfigurationProfile profile,
        CancellationToken cancellationToken = default);
    Task<bool> RestoreLatestAsync(PlatformKind platform, CancellationToken cancellationToken = default);
}

/// <summary>Applies documented PCSX2 global settings while preserving every key CLC does not own.</summary>
public sealed class Pcsx2ConfigurationAdapter : IEmulatorConfigurationAdapter
{
    public const int CurrentProfileSchemaVersion = 1;
    private const int MaximumConfigurationBytes = 4 * 1024 * 1024;
    private readonly string _mediaRoot;
    private readonly string _stateRoot;
    private readonly IEmulatorRegistryStore _registry;
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectNullableAnnotations = true };
    private sealed record BackupManifest(int SchemaVersion, string? BackupFileName, bool OriginalExisted, DateTimeOffset CreatedAt);

    public string EmulatorId => "pcsx2";
    public int AdapterVersion => 1;
    public IReadOnlyList<SimpleEmulationPreset> Presets => SimpleEmulationPresetCatalog.All;

    public Pcsx2ConfigurationAdapter(string mediaRoot, string stateRoot)
    {
        _mediaRoot = Path.GetFullPath(mediaRoot);
        _stateRoot = Path.GetFullPath(stateRoot);
        _registry = new EmulatorRegistryStore(stateRoot);
    }

    public EmulatorConfigurationProfile Translate(SimpleEmulationPreset preset)
    {
        ValidatePreset(preset);
        var upscale = preset.Resolution switch
        {
            EmulationResolutionGoal.Original => 1,
            EmulationResolutionGoal.Hd => 2,
            EmulationResolutionGoal.FullHd => 3,
            EmulationResolutionGoal.UltraHd => 6,
            _ => throw new InvalidDataException("Unsupported resolution goal.")
        };
        var aspect = preset.Aspect == EmulationAspectGoal.Widescreen ? "16:9" : "Auto 4:3/3:2";
        var profile = CreateProfile(preset.Id, preset.DisplayName, preset.Description,
            upscale, aspect, preset.WidescreenEnhancements);
        return Customize(profile, upscale, -1, aspect, preset.StartFullscreen, preset.Vsync,
            preset.WidescreenEnhancements);
    }

    public EmulatorConfigurationProfile Customize(SimpleEmulationPreset basis, int upscaleMultiplier,
        int renderer, string aspectRatio, bool fullscreen, bool vsync, bool widescreenPatches)
    {
        return Customize(Translate(basis), upscaleMultiplier, renderer, aspectRatio, fullscreen, vsync, widescreenPatches);
    }

    private EmulatorConfigurationProfile Customize(EmulatorConfigurationProfile basis, int upscaleMultiplier,
        int renderer, string aspectRatio, bool fullscreen, bool vsync, bool widescreenPatches)
    {
        if (upscaleMultiplier is < 1 or > 8) throw new InvalidDataException("Internal resolution must be between 1× and 8×.");
        if (renderer is not (-1 or 3 or 12 or 14 or 15)) throw new InvalidDataException("Unsupported PCSX2 renderer.");
        if (aspectRatio is not ("Auto 4:3/3:2" or "4:3" or "16:9" or "Stretch"))
            throw new InvalidDataException("Unsupported PCSX2 aspect ratio.");
        var values = BaseValues(upscaleMultiplier, aspectRatio, widescreenPatches).ToList();
        Set(values, "EmuCore/GS", "Renderer", renderer.ToString(System.Globalization.CultureInfo.InvariantCulture), "Renderer");
        Set(values, "UI", "StartFullscreen", Bool(fullscreen), "Start games fullscreen");
        Set(values, "EmuCore/GS", "VsyncEnable", vsync ? "1" : "0", "Vertical sync");
        return basis with { Values = values };
    }

    public async Task<EmulatorConfigurationPreview> PreviewAsync(PlatformKind platform,
        EmulatorConfigurationProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profile);
        ValidateForPlatform(profile, platform);
        var installation = await InstallationAsync(platform, cancellationToken);
        var path = ConfigurationPath(installation);
        var document = await IniDocument.LoadAsync(path, cancellationToken);
        var effectiveProfile = AddKnownSetupValues(profile, document);
        var changes = effectiveProfile.Values.Where(value => NeedsChange(document, value))
            .Select(value => new EmulatorConfigurationChange(value.Section, value.Key, value.DisplayName,
                PreviousValue(document, value), value.Value))
            .ToArray();
        return new(effectiveProfile, platform, path, changes, File.Exists(ManifestPath(platform)));
    }

    public async Task<EmulatorConfigurationApplyResult> ApplyAsync(PlatformKind platform,
        EmulatorConfigurationProfile profile, CancellationToken cancellationToken = default)
    {
        SharedEmulatorResourceLayout.Prepare(_mediaRoot, "PCSX2");
        var preview = await PreviewAsync(platform, profile, cancellationToken);
        EnsureNotRunning(platform);
        if (preview.Changes.Count == 0)
            return new(preview, DateTimeOffset.UtcNow, File.Exists(ManifestPath(platform)));
        var path = preview.ConfigurationPath;
        var existed = File.Exists(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backupFolder = BackupFolder(platform);
        Directory.CreateDirectory(backupFolder);
        var backupName = existed ? $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.ini" : null;
        if (backupName is not null) File.Copy(path, EmulatorPathContract.Resolve(backupFolder, backupName));
        var manifest = new BackupManifest(1, backupName, existed, DateTimeOffset.UtcNow);
        await WriteAtomicAsync(ManifestPath(platform), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

        var document = await IniDocument.LoadAsync(path, cancellationToken);
        foreach (var value in preview.Profile.Values)
        {
            if (value.Behavior == EmulatorConfigurationValueBehavior.EnsureListContains)
                document.EnsureListContains(value.Section, value.Key, value.Value);
            else
                document.Set(value.Section, value.Key, value.Value);
        }
        EnsureNotRunning(platform);
        await WriteAtomicAsync(path, document.Serialize(), cancellationToken);
        TrimBackups(backupFolder);
        return new(preview, DateTimeOffset.UtcNow, true);
    }

    public async Task<bool> RestoreLatestAsync(PlatformKind platform, CancellationToken cancellationToken = default)
    {
        var manifestPath = ManifestPath(platform);
        if (!File.Exists(manifestPath)) return false;
        EnsureNotRunning(platform);
        if (new FileInfo(manifestPath).Length > 65536) throw new InvalidDataException("The PCSX2 backup record is too large.");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("The PCSX2 backup record is unreadable.");
        if (manifest.SchemaVersion != 1 || manifest.OriginalExisted != (manifest.BackupFileName is not null))
            throw new InvalidDataException("The PCSX2 backup record is invalid.");
        var installation = await InstallationAsync(platform, cancellationToken);
        var path = ConfigurationPath(installation);
        if (manifest.OriginalExisted)
        {
            EmulatorPathContract.ValidateRelativePath(manifest.BackupFileName!);
            var backup = EmulatorPathContract.Resolve(BackupFolder(platform), manifest.BackupFileName!);
            if (!File.Exists(backup)) throw new IOException("The previous PCSX2 configuration backup is missing.");
            await WriteAtomicBytesAsync(path, await File.ReadAllBytesAsync(backup, cancellationToken), cancellationToken);
        }
        else if (File.Exists(path)) File.Delete(path);
        File.Delete(manifestPath);
        return true;
    }

    private async Task<EmulatorInstallation> InstallationAsync(PlatformKind platform, CancellationToken token)
    {
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux))
            throw new NotSupportedException("PCSX2 configuration supports Windows and Linux only.");
        return (await _registry.LoadAsync(token)).Installations.SingleOrDefault(item =>
            item.EmulatorId == EmulatorId && item.Platform == platform)
            ?? throw new IOException($"PCSX2 is not installed for {platform}.");
    }

    private string ConfigurationPath(EmulatorInstallation installation)
    {
        var executable = EmulatorPathContract.Resolve(_mediaRoot, installation.ExecutableRelativePath);
        if (!File.Exists(executable)) throw new IOException("The recorded PCSX2 executable is missing.");
        var folder = Path.GetDirectoryName(executable)!;
        if (installation.Platform == PlatformKind.Windows)
            return EmulatorPathContract.Resolve(folder, "inis/PCSX2.ini");
        var configRoot = executable + ".config";
        return EmulatorPathContract.Resolve(configRoot, "PCSX2/inis/PCSX2.ini");
    }

    private string BackupFolder(PlatformKind platform) => EmulatorPathContract.Resolve(_stateRoot,
        $"Config/EmulatorCompanion/Backups/pcsx2/{platform}");
    private string ManifestPath(PlatformKind platform) => EmulatorPathContract.Resolve(BackupFolder(platform), "latest.json");

    private static EmulatorConfigurationProfile CreateProfile(string id, string name, string description,
        int upscale, string aspect, bool widescreen) => new(CurrentProfileSchemaVersion, "pcsx2", id, name,
        description, EmulatorConfigurationScope.Global, BaseValues(upscale, aspect, widescreen));

    private static IReadOnlyList<EmulatorConfigurationValue> BaseValues(int upscale, string aspect, bool widescreen) =>
    [
        new("UI", "StartFullscreen", "true", "Start games fullscreen"),
        new("UI", "ConfirmShutdown", "false", "Controller-friendly exit"),
        new("Folders", "Bios", SharedEmulatorResourceLayout.RelativeFromEmulator("BIOS", "PCSX2"), "Shared BIOS folder"),
        new("Folders", "MemoryCards", SharedEmulatorResourceLayout.RelativeFromEmulator("Saves", "PCSX2"), "Shared memory-card folder"),
        new("Folders", "Savestates", SharedEmulatorResourceLayout.RelativeFromEmulator("States", "PCSX2"), "Shared save-state folder"),
        new("Folders", "Snapshots", SharedEmulatorResourceLayout.RelativeFromEmulator("Screenshots", "PCSX2"), "Shared screenshot folder"),
        new("Folders", "Textures", SharedEmulatorResourceLayout.RelativeFromEmulator("TexturePacks", "PCSX2"), "Shared texture-pack folder"),
        new("Folders", "InputProfiles", "inputprofiles", "Portable controller-profile folder"),
        new("GameList", "RecursivePaths", "../../../Roms/PlayStation 2", "PlayStation 2 game folder",
            EmulatorConfigurationValueBehavior.EnsureListContains),
        new("EmuCore", "EnableFastBoot", "true", "Fast boot"),
        new("EmuCore", "EnablePerGameSettings", "true", "Per-game settings"),
        new("EmuCore", "EnableWideScreenPatches", Bool(widescreen), "Widescreen patches"),
        new("EmuCore/GS", "Renderer", "-1", "Renderer"),
        new("EmuCore/GS", "upscale_multiplier", upscale.ToString(System.Globalization.CultureInfo.InvariantCulture), "Internal resolution"),
        new("EmuCore/GS", "AspectRatio", aspect, "Aspect ratio"),
        new("EmuCore/GS", "VsyncEnable", "0", "Vertical sync")
    ];

    private static void Set(List<EmulatorConfigurationValue> values, string section, string key, string value, string name)
    {
        values.RemoveAll(item => item.Section == section && item.Key == key);
        values.Add(new(section, key, value, name));
    }
    private static string Bool(bool value) => value ? "true" : "false";
    private EmulatorConfigurationProfile AddKnownSetupValues(EmulatorConfigurationProfile profile, IniDocument document)
    {
        var biosFolder = SharedEmulatorResourceLayout.GetPath(_mediaRoot, "BIOS", "PCSX2");
        if (!Directory.Exists(biosFolder)) return profile;

        var selected = document.Get("Filenames", "BIOS");
        var selectedIsValid = !string.IsNullOrWhiteSpace(selected) &&
            string.Equals(Path.GetFileName(selected), selected, StringComparison.Ordinal) &&
            File.Exists(Path.Combine(biosFolder, selected));
        if (selectedIsValid) return profile;

        var candidates = new DirectoryInfo(biosFolder).EnumerateFiles()
            .Where(file => file.LinkTarget is null && (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                file.Length is > 0 and <= 16 * 1024 * 1024 &&
                (file.Extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
                 file.Extension.Equals(".rom", StringComparison.OrdinalIgnoreCase)))
            .Take(2).ToArray();
        if (candidates.Length != 1) return profile;
        var values = profile.Values.ToList();
        Set(values, "Filenames", "BIOS", candidates[0].Name, "Selected BIOS");
        return profile with { Values = values };
    }
    private static bool NeedsChange(IniDocument document, EmulatorConfigurationValue value) =>
        value.Behavior == EmulatorConfigurationValueBehavior.EnsureListContains
            ? !document.GetAll(value.Section, value.Key).Contains(value.Value, StringComparer.OrdinalIgnoreCase)
            : !string.Equals(document.Get(value.Section, value.Key), value.Value, StringComparison.Ordinal);
    private static string? PreviousValue(IniDocument document, EmulatorConfigurationValue value)
    {
        if (value.Behavior != EmulatorConfigurationValueBehavior.EnsureListContains)
            return document.Get(value.Section, value.Key);
        var values = document.GetAll(value.Section, value.Key);
        return values.Count == 0 ? null : string.Join("; ", values);
    }
    private static void ValidatePreset(SimpleEmulationPreset preset)
    {
        if (preset.SchemaVersion != SimpleEmulationPresetCatalog.CurrentSchemaVersion ||
            !SimpleEmulationPresetCatalog.All.Any(item => item == preset))
            throw new InvalidDataException("The simple emulation preset is invalid.");
    }
    private static void ValidateProfile(EmulatorConfigurationProfile profile)
    {
        if (profile.SchemaVersion != CurrentProfileSchemaVersion || profile.EmulatorId != "pcsx2" ||
            profile.Scope != EmulatorConfigurationScope.Global || profile.Values.Count is <= 0 or > 64 ||
            profile.Values.GroupBy(value => (value.Section, value.Key)).Any(group => group.Count() != 1) ||
            profile.Values.Any(value => string.IsNullOrWhiteSpace(value.Section) || string.IsNullOrWhiteSpace(value.Key) ||
                value.Section.ContainsAny(['[', ']', '\r', '\n']) || value.Key.ContainsAny(['=', '\r', '\n']) ||
                value.Value.ContainsAny(['\r', '\n'])))
            throw new InvalidDataException("The PCSX2 configuration profile is invalid.");
    }
    private static void ValidateForPlatform(EmulatorConfigurationProfile profile, PlatformKind platform)
    {
        var renderer = profile.Values.Single(value => value.Section == "EmuCore/GS" && value.Key == "Renderer").Value;
        if (platform == PlatformKind.Linux && renderer is "3" or "15")
            throw new InvalidDataException("Direct3D renderers are available on Windows only.");
    }

    private static async Task WriteAtomicAsync(string path, string contents, CancellationToken token) =>
        await WriteAtomicBytesAsync(path, new UTF8Encoding(false).GetBytes(contents), token);
    private static async Task WriteAtomicBytesAsync(string path, byte[] contents, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await output.WriteAsync(contents, token);
                await output.FlushAsync(token);
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void TrimBackups(string folder)
    {
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.ini")
                     .OrderByDescending(file => file.CreationTimeUtc).Skip(10)) file.Delete();
    }

    private static void EnsureNotRunning(PlatformKind platform)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var matches = false;
                try
                {
                    if (!process.ProcessName.StartsWith("pcsx2", StringComparison.OrdinalIgnoreCase)) continue;
                    matches = true;
                    throw new IOException("Close PCSX2 before changing or restoring its configuration.");
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error) when (matches)
                { throw new IOException("A running PCSX2 process could not be checked. Close it before continuing.", error); }
            }
        }
    }

    private sealed class IniDocument
    {
        private readonly List<string> _lines;
        public string SourcePath { get; }
        private IniDocument(string sourcePath, List<string> lines) { SourcePath = sourcePath; _lines = lines; }
        public static async Task<IniDocument> LoadAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new(path, []);
            if (new FileInfo(path).Length > MaximumConfigurationBytes)
                throw new InvalidDataException("The PCSX2 configuration is too large to edit safely.");
            var text = await File.ReadAllTextAsync(path, token);
            return new(path, text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList());
        }

        public string? Get(string section, string key)
        {
            var range = SectionRange(section);
            if (range is null) return null;
            for (var index = range.Value.Start + 1; index < range.Value.End; index++)
                if (TryKey(_lines[index], out var found, out var value) && found.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return value;
            return null;
        }

        public IReadOnlyList<string> GetAll(string section, string key)
        {
            var range = SectionRange(section);
            if (range is null) return [];
            var values = new List<string>();
            for (var index = range.Value.Start + 1; index < range.Value.End; index++)
                if (TryKey(_lines[index], out var found, out var value) && found.Equals(key, StringComparison.OrdinalIgnoreCase))
                    values.Add(value);
            return values;
        }

        public void Set(string section, string key, string value)
        {
            var range = SectionRange(section);
            if (range is null)
            {
                if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
                _lines.Add($"[{section}]");
                _lines.Add($"{key} = {value}");
                return;
            }
            for (var index = range.Value.Start + 1; index < range.Value.End; index++)
            {
                if (!TryKey(_lines[index], out var found, out _ ) || !found.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                _lines[index] = $"{key} = {value}";
                return;
            }
            _lines.Insert(range.Value.End, $"{key} = {value}");
        }

        public void EnsureListContains(string section, string key, string value)
        {
            if (GetAll(section, key).Contains(value, StringComparer.OrdinalIgnoreCase)) return;
            var range = SectionRange(section);
            if (range is null)
            {
                Set(section, key, value);
                return;
            }
            _lines.Insert(range.Value.End, $"{key} = {value}");
        }

        public string Serialize() => string.Join(Environment.NewLine, _lines).TrimEnd() + Environment.NewLine;
        private (int Start, int End)? SectionRange(string section)
        {
            var start = -1;
            for (var index = 0; index < _lines.Count; index++)
            {
                var line = _lines[index].Trim();
                if (!line.StartsWith('[') || !line.EndsWith(']')) continue;
                if (start >= 0) return (start, index);
                if (line[1..^1].Equals(section, StringComparison.OrdinalIgnoreCase)) start = index;
            }
            return start >= 0 ? (start, _lines.Count) : null;
        }
        private static bool TryKey(string line, out string key, out string value)
        {
            key = value = "";
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is '#' or ';') return false;
            var equals = line.IndexOf('=');
            if (equals <= 0) return false;
            key = line[..equals].Trim();
            value = line[(equals + 1)..].Trim();
            return key.Length > 0;
        }
    }
}
