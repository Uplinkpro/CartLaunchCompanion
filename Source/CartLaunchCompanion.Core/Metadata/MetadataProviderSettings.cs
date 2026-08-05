using System.Text.Json;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Metadata;

public sealed class MetadataProviderSettings
{
    public string SteamGridDbApiKey { get; set; } = "";

    public static async Task<MetadataProviderSettings> LoadAsync(
        PortablePaths paths,
        CancellationToken cancellationToken = default)
    {
        var result = new MetadataProviderSettings();
        var path = Path.Combine(paths.Config, "metadata.json");

        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(
                    path,
                    cancellationToken);

                result =
                    JsonSerializer.Deserialize<MetadataProviderSettings>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? result;
            }
            catch (JsonException)
            {
                // A malformed optional settings file must not block the library.
            }
        }

        var environmentKey = Environment.GetEnvironmentVariable(
            "CLC_STEAMGRIDDB_API_KEY");

        if (!string.IsNullOrWhiteSpace(environmentKey))
            result.SteamGridDbApiKey = environmentKey.Trim();

        return result;
    }
}
