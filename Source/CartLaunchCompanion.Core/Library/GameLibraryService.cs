using System.Text.Json;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Migration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Library;

public sealed class GameLibraryService(
    GameConfigurationValidator validator,
    Version1GameConfigurationImporter version1Importer,
    IGamePathResolver pathResolver,
    ILaunchTargetSelector launchTargetSelector)
    : IGameLibraryService
{
    private static readonly string[] ConfigurationNames =
    [
        "game.json",
        "Game.json"
    ];

    public async Task<GameLibraryLoadResult> LoadAsync(
        PortablePaths paths,
        PlatformKind platform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var result = new GameLibraryLoadResult();

        if (!Directory.Exists(paths.Games))
        {
            Directory.CreateDirectory(paths.Games);
            return result;
        }

        var folders = Directory
            .EnumerateDirectories(
                paths.Games,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(
                folder => !string.Equals(
                    Path.GetFileName(folder),
                    "Examples",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(
                folder => Path.GetFileName(folder),
                StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configurationPath =
                FindConfigurationPath(folder);

            if (configurationPath is null)
            {
                result.Failures.Add(
                    new GameLibraryFailure(
                        folder,
                        null,
                        "No game.json file was found."));
                continue;
            }

            try
            {
                var entry = await LoadEntryAsync(
                    folder,
                    configurationPath,
                    platform,
                    cancellationToken);

                result.Games.Add(entry);
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException)
            {
                result.Failures.Add(
                    new GameLibraryFailure(
                        folder,
                        configurationPath,
                        ex.Message,
                        ex));
            }
        }

        result.Games.Sort(
            static (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(
                    GetSortName(left.Configuration),
                    GetSortName(right.Configuration)));

        return result;
    }

    private async Task<GameLibraryEntry> LoadEntryAsync(
        string folder,
        string configurationPath,
        PlatformKind platform,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(
            configurationPath,
            cancellationToken);

        var isVersion2 = IsVersion2(json);
        GameConfiguration configuration;
        List<string> migrationWarnings = [];

        if (isVersion2)
        {
            configuration =
                GameConfigurationJson.Deserialize(json);
        }
        else
        {
            var importResult = version1Importer.Import(json);
            configuration = importResult.Configuration;

            migrationWarnings.Add(
                "This game is using the Version 1 compatibility importer.");

            if (importResult.UnmappedFields.Count > 0)
            {
                migrationWarnings.Add(
                    "Unmapped Version 1 fields: " +
                    string.Join(", ", importResult.UnmappedFields));
            }

            migrationWarnings.AddRange(importResult.Warnings);
        }

        var validation = validator.Validate(configuration);
        var launchTarget =
            launchTargetSelector.Select(configuration, platform);

        var entry = new GameLibraryEntry
        {
            FolderPath = folder,
            ConfigurationPath = configurationPath,
            Configuration = configuration,
            ImportedFromVersion1 = !isVersion2,
            CoverPath = pathResolver.ResolveExisting(
                folder,
                configuration.Artwork.Cover),
            BackgroundPath = pathResolver.ResolveExisting(
                folder,
                configuration.Artwork.Background),
            LogoPath = pathResolver.ResolveExisting(
                folder,
                configuration.Artwork.Logo),
            IconPath = pathResolver.ResolveExisting(
                folder,
                configuration.Artwork.Icon),
            TrailerPath = pathResolver.ResolveExisting(
                folder,
                configuration.Artwork.Trailer),
            LaunchTarget = ResolveLaunchPaths(
                folder,
                launchTarget)
        };

        entry.ValidationIssues.AddRange(validation.Issues);
        entry.Warnings.AddRange(migrationWarnings);

        AddMissingAssetWarnings(entry);
        AddPlatformWarnings(entry, platform);

        return entry;
    }

    private LaunchTargetSelection? ResolveLaunchPaths(
        string folder,
        LaunchTargetSelection? target)
    {
        if (target is null)
            return null;

        return target with
        {
            Executable = pathResolver.Resolve(
                folder,
                target.Executable),
            WorkingDirectory = pathResolver.Resolve(
                folder,
                target.WorkingDirectory),
            WinePrefix = pathResolver.Resolve(
                folder,
                target.WinePrefix)
        };
    }

    private static bool IsVersion2(string json)
    {
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        if (!document.RootElement.TryGetProperty(
                "formatVersion",
                out var formatVersion))
        {
            return false;
        }

        return formatVersion.ValueKind == JsonValueKind.Number &&
               formatVersion.TryGetInt32(out var version) &&
               version == 2;
    }

    private static string? FindConfigurationPath(string folder)
    {
        foreach (var name in ConfigurationNames)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string GetSortName(GameConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration.Game.SortName)
            ? configuration.Game.Name
            : configuration.Game.SortName;

    private static void AddMissingAssetWarnings(GameLibraryEntry entry)
    {
        if (entry.CoverPath is null)
        {
            entry.Warnings.Add(
                "Cover artwork is missing.");
        }

        if (entry.BackgroundPath is null)
        {
            entry.Warnings.Add(
                "Background artwork is missing.");
        }

        if (!string.IsNullOrWhiteSpace(
                entry.Configuration.Artwork.Trailer) &&
            entry.TrailerPath is null)
        {
            entry.Warnings.Add(
                "The configured trailer file is missing.");
        }
    }

    private static void AddPlatformWarnings(
        GameLibraryEntry entry,
        PlatformKind platform)
    {
        if (platform == PlatformKind.Unsupported)
        {
            entry.Warnings.Add(
                "The current operating system is unsupported.");
            return;
        }

        if (entry.LaunchTarget is null)
        {
            entry.Warnings.Add(
                "No launch target could be selected for this platform.");
            return;
        }

        if (!entry.LaunchTarget.Enabled)
        {
            entry.Warnings.Add(
                $"This game is disabled on {platform}.");
        }
    }
}
