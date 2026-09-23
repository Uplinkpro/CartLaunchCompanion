using System.Text.Json;
using System.Text.Json.Serialization;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Strict catalog and registry contracts. Invalid data is never treated as an empty document.</summary>
public static class EmulatorManagementJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        Converters =
        {
            new JsonStringEnumConverter<PlatformKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<EmulatorReleaseSourceKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new JsonStringEnumConverter<EmulatorPrereleasePolicy>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };

    public static EmulatorCatalog ReadCatalog(string json)
    {
        var catalog = JsonSerializer.Deserialize<EmulatorCatalog>(json, Options)
            ?? throw new InvalidDataException("The catalog cannot be null.");
        var legacy = catalog.SchemaVersion == 1;
        if (legacy)
            catalog = catalog with { SchemaVersion = EmulatorCatalog.CurrentSchemaVersion };
        Validate(catalog);
        if (legacy)
            ValidateLegacyCatalogShape(json);
        return catalog;
    }

    public static string WriteCatalog(EmulatorCatalog catalog)
    {
        Validate(catalog);
        return JsonSerializer.Serialize(catalog, Options);
    }

    public static EmulatorRegistry ReadRegistry(string json)
    {
        var registry = JsonSerializer.Deserialize<EmulatorRegistry>(json, Options)
            ?? throw new InvalidDataException("The registry cannot be null.");
        Validate(registry);
        return registry;
    }

    public static string WriteRegistry(EmulatorRegistry registry)
    {
        Validate(registry);
        return JsonSerializer.Serialize(registry, Options);
    }

    public static void Validate(EmulatorCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.SchemaVersion != EmulatorCatalog.CurrentSchemaVersion || catalog.Emulators is null)
            throw new InvalidDataException("Unsupported or invalid emulator catalog schema.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var emulator in catalog.Emulators)
        {
            if (emulator is null)
                throw new InvalidDataException("Catalog entries cannot be null.");
            ValidateId(emulator.Id);
            if (!ids.Add(emulator.Id) || string.IsNullOrWhiteSpace(emulator.DisplayName) ||
                emulator.SystemIds is null || emulator.ReleaseChannels is null)
                throw new InvalidDataException("Catalog entries require unique IDs, names, systems, and channels.");
            var systems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var system in emulator.SystemIds)
            {
                ValidateId(system);
                if (!systems.Add(system))
                    throw new InvalidDataException("System IDs must be unique within an emulator.");
            }
            var channels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in emulator.ReleaseChannels)
            {
                if (channel is null)
                    throw new InvalidDataException("Channels cannot be null.");
                ValidateId(channel.Id);
                if (!channels.Add(channel.Id) || string.IsNullOrWhiteSpace(channel.DisplayName))
                    throw new InvalidDataException("Channels require unique IDs and display names.");
            }
            EmulatorCatalogMetadataValidator.Validate(emulator);
        }
    }

    private static void ValidateLegacyCatalogShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var entry in document.RootElement.GetProperty("emulators").EnumerateArray())
        {
            foreach (var property in entry.EnumerateObject())
                if (property.Name is not ("id" or "displayName" or "systemIds" or "releaseChannels"))
                    throw new InvalidDataException("Extended metadata requires catalog schema version 2.");
            if (entry.TryGetProperty("releaseChannels", out var channels))
                foreach (var channel in channels.EnumerateArray())
                    foreach (var property in channel.EnumerateObject())
                        if (property.Name is not ("id" or "displayName"))
                            throw new InvalidDataException("Extended channel metadata requires catalog schema version 2.");
        }
    }

    public static void Validate(EmulatorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry.SchemaVersion != EmulatorRegistry.CurrentSchemaVersion || registry.Installations is null)
            throw new InvalidDataException("Unsupported or invalid emulator registry schema.");
        var keys = new HashSet<(string, PlatformKind)>();
        foreach (var installation in registry.Installations)
        {
            ValidateInstallation(installation);
            if (!keys.Add((installation.EmulatorId, installation.Platform)))
                throw new InvalidDataException("Duplicate emulator installation for the same platform.");
        }
    }

    public static void ValidateInstallation(EmulatorInstallation installation)
    {
        if (installation is null)
            throw new InvalidDataException("Installations cannot be null.");
        ValidateId(installation.EmulatorId);
        EmulatorPathContract.ValidateExecutablePath(installation.Platform, installation.ExecutableRelativePath);
        if (installation.InstalledChannelId is not null)
            ValidateId(installation.InstalledChannelId);
        if (installation.InstalledVersion is not null && string.IsNullOrWhiteSpace(installation.InstalledVersion))
            throw new InvalidDataException("Unknown installed versions must be null, not blank.");
    }

    internal static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9') ||
            id.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')))
            throw new InvalidDataException("IDs must be lowercase ASCII identifiers, starting with a letter or digit.");
    }
}
