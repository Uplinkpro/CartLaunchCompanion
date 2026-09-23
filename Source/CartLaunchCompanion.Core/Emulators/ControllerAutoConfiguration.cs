using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

public enum ControllerLayoutKind { Xbox, PlayStation, Nintendo, Generic }
public enum ControllerFaceButtonPreference { PhysicalPosition, PrintedLabels }
public enum ControllerMappingReadiness { ReadyToApply, NeedsInputTest, NeedsManualSetup }

/// <summary>A controller found directly through SDL. Steam Input is not part of this path.</summary>
public sealed record DetectedGameController(
    string DeviceId,
    string DisplayName,
    ControllerLayoutKind Layout,
    bool HasStandardSdlMapping,
    bool SupportsRumble,
    ControllerFamilyKind Family = ControllerFamilyKind.GenericXboxStyle,
    ushort VendorId = 0,
    ushort ProductId = 0,
    ControllerMappingKind Mapping = ControllerMappingKind.SdlStandard,
    int DeviceOrdinal = 1);

public enum CanonicalControllerInput
{
    South, East, West, North,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    LeftShoulder, RightShoulder, LeftTrigger, RightTrigger,
    LeftStickX, LeftStickY, RightStickX, RightStickY,
    LeftStickClick, RightStickClick, Start, Back, Guide
}

public sealed record CanonicalControllerBinding(CanonicalControllerInput Input, string SdlBinding);

public sealed record ControllerAutoConfigurationRequest(
    DetectedGameController Controller,
    int PlayerNumber,
    ControllerFaceButtonPreference FaceButtonPreference,
    IReadOnlyList<CanonicalControllerBinding> Bindings);

public sealed record ControllerConfigurationPlan(
    string EmulatorId,
    PlatformKind Platform,
    ControllerAutoConfigurationRequest Request,
    ControllerMappingReadiness Readiness,
    IReadOnlyList<EmulatorConfigurationValue> NativeChanges,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Translates a verified SDL layout into one emulator's native bindings. A plan
/// must pass an interactive input test before the caller records it as ready.
/// </summary>
public interface IEmulatorControllerConfigurationAdapter
{
    string EmulatorId { get; }
    Task<ControllerConfigurationPlan> PreviewAsync(PlatformKind platform,
        ControllerAutoConfigurationRequest request, CancellationToken cancellationToken = default);
    Task ApplyAsync(ControllerConfigurationPlan plan, CancellationToken cancellationToken = default);
}

public sealed record VerifiedControllerProfile(
    int SchemaVersion,
    string EmulatorId,
    PlatformKind Platform,
    DetectedGameController Controller,
    ControllerFaceButtonPreference FaceButtonPreference,
    IReadOnlyList<CanonicalControllerBinding> Bindings,
    IReadOnlyList<CanonicalControllerInput> VerifiedInputs,
    DateTimeOffset VerifiedAt,
    int PlayerNumber = 1);

public sealed class VerifiedControllerProfileStore(string stateRoot)
{
    public const int CurrentSchemaVersion = 1;
    public const string SharedProfileId = "library";
    public static string SharedProfileIdForPlayer(int playerNumber) => playerNumber switch
    {
        1 => SharedProfileId,
        2 => "library-player2",
        _ => throw new ArgumentOutOfRangeException(nameof(playerNumber))
    };
    private readonly string _root = Path.GetFullPath(stateRoot);
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
    };

    public async Task SaveAsync(VerifiedControllerProfile profile, CancellationToken token = default)
    {
        if (profile.SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(profile.EmulatorId) ||
            profile.Platform is not (PlatformKind.Windows or PlatformKind.Linux) ||
            profile.PlayerNumber is not (1 or 2) ||
            string.IsNullOrWhiteSpace(profile.Controller.DeviceId) || profile.Bindings.Count == 0 ||
            profile.Bindings.Select(item => item.Input).Distinct().Count() != profile.Bindings.Count ||
            profile.VerifiedInputs.Distinct().Count() != profile.VerifiedInputs.Count ||
            profile.VerifiedInputs.Any(input => profile.Bindings.All(binding => binding.Input != input)))
            throw new InvalidDataException("The verified controller profile is invalid.");
        EmulatorPathContract.ValidateRelativePath(profile.EmulatorId);
        var path = EmulatorPathContract.Resolve(_root,
            $"Config/EmulatorCompanion/Controllers/{profile.EmulatorId}/{profile.Platform}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, System.Text.Json.JsonSerializer.Serialize(profile, Options), token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<VerifiedControllerProfile?> LoadAsync(string emulatorId, PlatformKind platform,
        CancellationToken token = default)
    {
        EmulatorPathContract.ValidateRelativePath(emulatorId);
        var path = EmulatorPathContract.Resolve(_root,
            $"Config/EmulatorCompanion/Controllers/{emulatorId}/{platform}.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("The controller profile is too large.");
        var profile = System.Text.Json.JsonSerializer.Deserialize<VerifiedControllerProfile>(
            await File.ReadAllTextAsync(path, token), Options)
            ?? throw new InvalidDataException("The controller profile is unreadable.");
        if (profile.PlayerNumber == 0) profile = profile with { PlayerNumber = 1 };
        if (profile.SchemaVersion != CurrentSchemaVersion || profile.EmulatorId != emulatorId || profile.Platform != platform)
            throw new InvalidDataException("The controller profile is incompatible.");
        if (emulatorId == SharedProfileIdForPlayer(2) && profile.PlayerNumber != 2)
            throw new InvalidDataException("The Player 2 controller profile is incompatible.");
        return profile;
    }
}
