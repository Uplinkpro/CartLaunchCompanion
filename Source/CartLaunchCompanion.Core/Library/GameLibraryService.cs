using System.Text.Json;
using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Configuration.Migration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Metadata;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Library;

public sealed class GameLibraryService(
    GameConfigurationValidator validator,
    Version1GameConfigurationImporter version1Importer,
    IGamePathResolver pathResolver,
    ILaunchTargetSelector launchTargetSelector,
    IGameMetadataService? metadataService = null)
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
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var loadTasks = folders.Select(
            folder => LoadFolderAsync(
                folder,
                paths,
                platform,
                cancellationToken));

        var folderResults = await Task.WhenAll(loadTasks);

        foreach (var folderResult in folderResults)
        {
            if (folderResult.Entry is not null)
                result.Games.Add(folderResult.Entry);

            if (folderResult.Failure is not null)
                result.Failures.Add(folderResult.Failure);
        }

        result.Games.Sort(
            static (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(
                    GetSortName(left.Configuration),
                    GetSortName(right.Configuration)));

        return result;
    }

    private async Task<FolderLoadResult> LoadFolderAsync(
        string folder,
        PortablePaths paths,
        PlatformKind platform,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configurationPath = FindConfigurationPath(folder);

        if (configurationPath is null)
        {
            return new FolderLoadResult(
                null,
                new GameLibraryFailure(
                    folder,
                    null,
                    "No game.json file was found."));
        }

        try
        {
            var entry = await LoadEntryAsync(
                folder,
                configurationPath,
                paths,
                platform,
                cancellationToken);

            return new FolderLoadResult(entry, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FolderLoadResult(
                null,
                new GameLibraryFailure(
                    folder,
                    configurationPath,
                    ex.Message,
                    ex));
        }
    }

    private async Task<GameLibraryEntry> LoadEntryAsync(
        string folder,
        string configurationPath,
        PortablePaths portablePaths,
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

        if (metadataService is not null)
        {
            var metadata = await metadataService.EnrichAsync(
                folder,
                configuration,
                portablePaths,
                cancellationToken);

            migrationWarnings.AddRange(metadata.Warnings);
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
            CoverPath = pathResolver.ResolveExistingWithAnyExtension(
                folder,
                configuration.Artwork.Cover),
            BackgroundPath = pathResolver.ResolveExistingWithAnyExtension(
                folder,
                configuration.Artwork.Background),
            LogoPath = pathResolver.ResolveExistingWithAnyExtension(
                folder,
                configuration.Artwork.Logo),
            IconPath = pathResolver.ResolveExistingWithAnyExtension(
                folder,
                configuration.Artwork.Icon),
            TrailerPath = ResolveLocalTrailerPath(folder, configuration),
            TrailerSource = ResolveTrailerSource(folder, configuration),
            ScreenshotPaths = ResolveScreenshotPaths(folder),
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

    private static IReadOnlyList<string> ResolveScreenshotPaths(string folder)
    {
        var screenshotFolder = Path.Combine(folder, "Artwork", "Screenshots");
        if (!Directory.Exists(screenshotFolder))
            return [];

        // Let the desktop image decoder determine support instead of limiting
        // screenshots by their file extension.
        return Directory.EnumerateFiles(screenshotFolder)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? ResolveLocalTrailerPath(
        string folder,
        GameConfiguration configuration)
    {
        var snap = pathResolver.ResolveExisting(folder, "Media/Snap.mp4");
        if (snap is not null)
            return snap;

        var configured = pathResolver.ResolveExisting(
            folder,
            configuration.Artwork.Trailer);
        if (configured is not null)
            return configured;

        return pathResolver.ResolveExisting(folder, "Media/SteamTrailer.mp4") ??
               pathResolver.ResolveExisting(folder, "Media/SteamTrailer.webm");
    }

    private string? ResolveTrailerSource(
        string folder,
        GameConfiguration configuration) =>
        ResolveLocalTrailerPath(folder, configuration) ??
        (string.IsNullOrWhiteSpace(configuration.Artwork.TrailerUrl)
            ? null
            : configuration.Artwork.TrailerUrl.Trim());

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
                target.WinePrefix),
            CompanionApplication = new CompanionApplicationConfiguration
            {
                Enabled = target.CompanionApplication.Enabled,
                Executable = pathResolver.Resolve(folder, target.CompanionApplication.Executable),
                Arguments = target.CompanionApplication.Arguments,
                WorkingDirectory = pathResolver.Resolve(folder, target.CompanionApplication.WorkingDirectory),
                CloseAfterGame = target.CompanionApplication.CloseAfterGame
            }
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

    private sealed record FolderLoadResult(
        GameLibraryEntry? Entry,
        GameLibraryFailure? Failure);
}
