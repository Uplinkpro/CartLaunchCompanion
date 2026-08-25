using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.Configuration;

public static class GameConfigurationJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static async Task<GameConfiguration> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = File.OpenRead(filePath);

        var configuration =
            await JsonSerializer.DeserializeAsync<GameConfiguration>(
                stream,
                Options,
                cancellationToken);

        return configuration
            ?? throw new InvalidDataException(
                $"The game configuration is empty: {filePath}");
    }

    public static async Task SaveAsync(
        string filePath,
        GameConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.Game.Id))
            configuration.Game.Id = GameIdentity.Create();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = filePath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    configuration,
                    Options,
                    cancellationToken);

                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static string Serialize(GameConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return JsonSerializer.Serialize(configuration, Options);
    }

    public static GameConfiguration Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<GameConfiguration>(json, Options)
            ?? throw new InvalidDataException(
                "The game configuration is empty.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };

        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}
