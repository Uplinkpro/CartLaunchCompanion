using System.Text.Json;
using System.Text.Json.Serialization;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public sealed record EmulatorUpdatePreferences
{
    public int SchemaVersion { get; init; } = 1;
    public bool CheckBeforeLaunch { get; init; } = true;
    public int CheckIntervalHours { get; init; } = 24;
}

public interface IEmulatorUpdateSettings
{
    Task<EmulatorUpdatePreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(EmulatorUpdatePreferences preferences, CancellationToken cancellationToken = default);
    Task<string?> GetSkippedVersionAsync(PlatformKind platform, CancellationToken cancellationToken = default);
    Task ClearSkippedVersionAsync(PlatformKind platform, CancellationToken cancellationToken = default);
}

public sealed class EmulatorUpdateSettingsStore(string root) : IEmulatorUpdateSettings
{
    private const string RelativePath = "Config/emulator-update-preferences.json";
    public async Task<EmulatorUpdatePreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        var preferences = await EmulatorUpdateFiles.ReadAsync<EmulatorUpdatePreferences>(
            EmulatorPathContract.Resolve(root, RelativePath), cancellationToken) ?? new();
        Validate(preferences);
        return preferences;
    }

    public async Task SaveAsync(EmulatorUpdatePreferences preferences, CancellationToken cancellationToken = default)
    {
        Validate(preferences);
        var path = EmulatorPathContract.Resolve(root, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new FileStream(EmulatorPathContract.Resolve(root, RelativePath + ".lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Never silently replace unsupported or corrupt preferences.
        await LoadAsync(cancellationToken);
        await EmulatorUpdateFiles.WriteAsync(path, preferences, cancellationToken);
    }

    public async Task<string?> GetSkippedVersionAsync(PlatformKind platform, CancellationToken cancellationToken = default) =>
        (await new PpssppUpdateStateStore(root).LoadAsync(platform, cancellationToken)).SkippedVersion;

    public async Task ClearSkippedVersionAsync(PlatformKind platform, CancellationToken cancellationToken = default)
    {
        var store = new PpssppUpdateStateStore(root);
        using var writer = store.Lock(platform);
        var state = await store.LoadAsync(platform, cancellationToken);
        await store.SaveAsync(platform, state with { SkippedVersion = null }, cancellationToken);
    }

    private static void Validate(EmulatorUpdatePreferences preferences)
    {
        if (preferences.SchemaVersion != 1 || preferences.CheckIntervalHours is not (6 or 24 or 168))
            throw new InvalidDataException("Unsupported update preferences. Existing settings were left unchanged.");
    }
}

internal sealed record PpssppUpdateState(int SchemaVersion = 1, DateTimeOffset? CheckedAt = null,
    DateTimeOffset? RetryAfter = null, EmulatorRelease? Release = null, string? SkippedVersion = null);

internal sealed class PpssppUpdateStateStore(string root)
{
    private string RelativePath(PlatformKind platform) => platform switch
    {
        PlatformKind.Windows => "Config/ppsspp-updates-Windows.json",
        PlatformKind.Linux => "Config/ppsspp-updates-Linux.json",
        _ => throw new InvalidDataException("Unsupported update platform.")
    };

    public FileStream Lock(PlatformKind platform)
    {
        var relative = RelativePath(platform);
        var path = EmulatorPathContract.Resolve(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(EmulatorPathContract.Resolve(root, relative + ".lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public async Task<PpssppUpdateState> LoadAsync(PlatformKind platform, CancellationToken token)
    {
        var state = await EmulatorUpdateFiles.ReadAsync<PpssppUpdateState>(
            EmulatorPathContract.Resolve(root, RelativePath(platform)), token) ?? new();
        if (state.SchemaVersion != 1) throw new InvalidDataException("Unsupported update cache.");
        if (state.SkippedVersion is { } version) PpssppInstaller.CompareVersions(version, version);
        if (state.Release is { } release)
        {
            PpssppInstaller.Validate(release);
            if (release.Platform != platform) throw new InvalidDataException("Cached package platform mismatch.");
        }
        return state;
    }

    public Task SaveAsync(PlatformKind platform, PpssppUpdateState state, CancellationToken token) =>
        EmulatorUpdateFiles.WriteAsync(EmulatorPathContract.Resolve(root, RelativePath(platform)), state, token);
}

internal static class EmulatorUpdateFiles
{
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true
    };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken token) where T : class
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, true);
            if (stream.Length > 1048576) throw new InvalidDataException("Update settings file is too large.");
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, token)
                ?? throw new InvalidDataException("Update settings cannot be empty.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, token);
                await stream.FlushAsync(token);
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}