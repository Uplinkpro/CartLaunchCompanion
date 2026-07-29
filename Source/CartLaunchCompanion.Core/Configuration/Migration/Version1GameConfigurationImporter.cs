using System.Text.Json;

namespace CartLaunchCompanion.Core.Configuration.Migration;

public sealed class Version1GameConfigurationImporter
{
    public Version1ImportResult Import(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Version 1 game configuration must be a JSON object.");
        }

        var source = ReadProperties(document.RootElement);
        var configuration = new GameConfiguration();
        var result = new Version1ImportResult
        {
            Configuration = configuration
        };

        configuration.Game.Name =
            TakeString(source, result, "Name") ?? "";

        configuration.Game.Description =
            TakeString(source, result, "Description") ?? "";

        configuration.Game.Developer =
            TakeString(source, result, "Developer") ?? "";

        configuration.Game.Publisher =
            TakeString(source, result, "Publisher") ?? "";

        configuration.Game.Genre =
            TakeString(source, result, "Genre") ?? "";

        configuration.Game.ReleaseDate =
            TakeString(source, result, "ReleaseDate") ?? "";

        configuration.Game.Players =
            TakeString(source, result, "Players") ?? "";

        configuration.Artwork.Cover =
            TakeString(source, result, "CoverImage", "CoverPath")
            ?? configuration.Artwork.Cover;

        configuration.Artwork.Background =
            TakeString(
                source,
                result,
                "HeaderImage",
                "HeaderPath",
                "BackgroundImage")
            ?? configuration.Artwork.Background;

        configuration.Artwork.Trailer =
            TakeString(source, result, "VideoFile")
            ?? configuration.Artwork.Trailer;

        configuration.Artwork.TrailerUrl =
            TakeString(source, result, "VideoUrl", "YouTubeUrl") ?? "";

        configuration.Artwork.SteamMetadataId =
            TakeString(
                source,
                result,
                "SteamMetadataID",
                "SteamMetadataId",
                "SteamID",
                "SteamId")
            ?? "";

        configuration.Launch.Windows.SteamId =
            TakeString(source, result, "SteamID", "SteamId") ?? "";

        configuration.Launch.Windows.XboxAppId =
            TakeString(source, result, "XboxAppId") ?? "";

        configuration.Launch.Windows.EpicAppName =
            TakeString(source, result, "EpicAppName") ?? "";

        configuration.Launch.Windows.GogGameId =
            TakeString(source, result, "GogGameId", "GOGGameId") ?? "";

        configuration.Launch.Windows.UbisoftGameId =
            TakeString(source, result, "UbisoftGameId") ?? "";

        configuration.Launch.Windows.RockstarGameId =
            TakeString(source, result, "RockstarGameId") ?? "";

        configuration.Launch.Windows.AmazonGameId =
            TakeString(source, result, "AmazonGameId") ?? "";

        configuration.Launch.Windows.Executable =
            TakeString(source, result, "Executable") ?? "";

        configuration.Launch.Windows.Arguments =
            TakeString(source, result, "Arguments") ?? "";

        configuration.Launch.Windows.WorkingDirectory =
            TakeString(source, result, "WorkingDirectory") ?? "";

        configuration.Launch.Windows.ProcessName =
            TakeString(source, result, "ProcessName") ?? "";

        configuration.Launch.Windows.Uri =
            TakeString(source, result, "Uri", "LaunchUri") ?? "";

        configuration.Behavior.RestoreLauncherAfterExit =
            TakeBoolean(source, result, "RestoreOnExit")
            ?? configuration.Behavior.RestoreLauncherAfterExit;

        configuration.Behavior.ProcessStartTimeoutSeconds =
            TakeInt32(source, result, "ProcessStartTimeoutSeconds")
            ?? configuration.Behavior.ProcessStartTimeoutSeconds;

        configuration.Behavior.ProcessExitPollSeconds =
            TakeInt32(source, result, "ProcessExitPollSeconds")
            ?? configuration.Behavior.ProcessExitPollSeconds;

        configuration.Launch.Windows.Launcher =
            ParseLauncher(
                TakeString(source, result, "Launcher"),
                configuration.Launch.Windows);

        // Version 1 is Windows-only. Do not imply that a Linux launch target
        // exists when importing it.
        configuration.Launch.Linux.Enabled = false;

        foreach (var property in source.Values)
        {
            if (!property.Consumed)
                result.UnmappedFields.Add(property.OriginalName);
        }

        if (string.IsNullOrWhiteSpace(configuration.Game.Name))
            result.Warnings.Add("The Version 1 configuration has no Name.");

        return result;
    }

    public async Task<Version1ImportResult> ImportFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var json = await File.ReadAllTextAsync(
            filePath,
            cancellationToken);

        return Import(json);
    }

    private static LauncherKind ParseLauncher(
        string? value,
        WindowsLaunchConfiguration launch)
    {
        if (Enum.TryParse<LauncherKind>(
                value,
                ignoreCase: true,
                out var parsed))
        {
            return parsed;
        }

        if (!string.IsNullOrWhiteSpace(launch.XboxAppId))
            return LauncherKind.Xbox;

        if (!string.IsNullOrWhiteSpace(launch.SteamId))
            return LauncherKind.Steam;

        if (!string.IsNullOrWhiteSpace(launch.EpicAppName))
            return LauncherKind.Epic;

        if (!string.IsNullOrWhiteSpace(launch.UbisoftGameId))
            return LauncherKind.Ubisoft;

        if (!string.IsNullOrWhiteSpace(launch.RockstarGameId))
            return LauncherKind.Rockstar;

        if (!string.IsNullOrWhiteSpace(launch.Executable))
            return LauncherKind.Local;

        return LauncherKind.Custom;
    }

    private static Dictionary<string, SourceProperty> ReadProperties(
        JsonElement root)
    {
        var properties =
            new Dictionary<string, SourceProperty>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            properties[property.Name] =
                new SourceProperty(
                    property.Name,
                    property.Value.Clone());
        }

        return properties;
    }

    private static string? TakeString(
        Dictionary<string, SourceProperty> source,
        Version1ImportResult result,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!source.TryGetValue(name, out var property) ||
                property.Consumed)
            {
                continue;
            }

            property.Consumed = true;
            result.ImportedFields.Add(property.OriginalName);

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static bool? TakeBoolean(
        Dictionary<string, SourceProperty> source,
        Version1ImportResult result,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!source.TryGetValue(name, out var property) ||
                property.Consumed)
            {
                continue;
            }

            property.Consumed = true;
            result.ImportedFields.Add(property.OriginalName);

            if (property.Value.ValueKind is JsonValueKind.True)
                return true;

            if (property.Value.ValueKind is JsonValueKind.False)
                return false;

            if (property.Value.ValueKind is JsonValueKind.String &&
                bool.TryParse(property.Value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? TakeInt32(
        Dictionary<string, SourceProperty> source,
        Version1ImportResult result,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!source.TryGetValue(name, out var property) ||
                property.Consumed)
            {
                continue;
            }

            property.Consumed = true;
            result.ImportedFields.Add(property.OriginalName);

            if (property.Value.TryGetInt32(out var number))
                return number;

            if (property.Value.ValueKind is JsonValueKind.String &&
                int.TryParse(property.Value.GetString(), out number))
            {
                return number;
            }
        }

        return null;
    }

    private sealed class SourceProperty(
        string originalName,
        JsonElement value)
    {
        public string OriginalName { get; } = originalName;
        public JsonElement Value { get; } = value;
        public bool Consumed { get; set; }
    }
}
