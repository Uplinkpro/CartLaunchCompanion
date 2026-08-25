using System.Text.Json;
using System.Text.Json.Serialization;
using CartLaunchCompanion.Core.Metadata;

namespace CartLaunchCompanion.Configurator;

public sealed class ConfiguratorSettings
{
    [JsonIgnore]
    public string SteamWebApiKey { get; set; } = "";
    [JsonIgnore]
    public string SteamGridDbApiKey { get; set; } = "";
    public bool SetupCompleted { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CartLaunchCompanion",
        "Configurator",
        "settings.json");

    public static async Task<ConfiguratorSettings> LoadAsync()
    {
        if (!File.Exists(FilePath))
        {
            return new ConfiguratorSettings
            {
                SteamWebApiKey = await MetadataSecretStore.ReadAsync(MetadataSecretStore.SteamWebApiKey),
                SteamGridDbApiKey = await MetadataSecretStore.ReadAsync(MetadataSecretStore.SteamGridDbApiKey)
            };
        }
        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            var result = JsonSerializer.Deserialize<ConfiguratorSettings>(json) ?? new ConfiguratorSettings();
            result.SteamWebApiKey = await MetadataSecretStore.ReadAsync(MetadataSecretStore.SteamWebApiKey);
            result.SteamGridDbApiKey = await MetadataSecretStore.ReadAsync(MetadataSecretStore.SteamGridDbApiKey);
            return result;
        }
        catch { return new ConfiguratorSettings(); }
    }

    public async Task SaveAsync()
    {
        await MetadataSecretStore.WriteAsync(MetadataSecretStore.SteamWebApiKey, SteamWebApiKey);
        await MetadataSecretStore.WriteAsync(MetadataSecretStore.SteamGridDbApiKey, SteamGridDbApiKey);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(this));
    }
}
