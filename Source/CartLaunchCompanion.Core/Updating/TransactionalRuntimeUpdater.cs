using System.Text.Json;

namespace CartLaunchCompanion.Core.Updating;

public sealed class TransactionalRuntimeUpdater(
    RuntimeIntegrityVerifier integrityVerifier,
    IUpdateSignatureVerifier signatureVerifier)
{
    private static readonly JsonSerializerOptions JournalOptions = new()
    {
        WriteIndented = true
    };

    private static readonly RuntimeUpdateJsonContext JournalJsonContext = new(JournalOptions);

    public async Task<RuntimeUpdateResult> ApplyAsync(
        RuntimeUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestPaths(request);

        var cartRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.CartRoot));
        await RecoverInterruptedUpdateAsync(cartRoot, cancellationToken);

        var manifest = await RuntimeUpdateManifestJson.LoadAsync(
            request.ManifestPath,
            cancellationToken);

        if (!string.Equals(manifest.Platform, request.Platform, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update platform does not match its destination.");
        }

        if (!manifest.IsSigned || !signatureVerifier.Verify(manifest))
        {
            throw new InvalidDataException("The update signature is missing or untrusted.");
        }

        await integrityVerifier.VerifyAsync(
            request.StagedRuntimeRoot,
            manifest,
            cancellationToken);

        var activeRoot = Path.Combine(cartRoot, "System", request.Platform);
        var maintenanceRoot = Path.Combine(cartRoot, ".cartlaunch");
        var backupRoot = Path.Combine(maintenanceRoot, "previous-runtime", request.Platform);
        var journalPath = Path.Combine(maintenanceRoot, "update-journal.json");

        Directory.CreateDirectory(maintenanceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(backupRoot)!);

        var previousVersion = TryReadAssemblyVersion(activeRoot, manifest.EntryPoint);
        var journal = new RuntimeUpdateJournal
        {
            Platform = request.Platform,
            PreviousVersion = previousVersion,
            NewVersion = manifest.Version,
            State = RuntimeUpdateState.Prepared
        };
        await WriteJournalAsync(journalPath, journal, cancellationToken);

        try
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }

            if (Directory.Exists(activeRoot))
            {
                Directory.Move(activeRoot, backupRoot);
            }

            journal.State = RuntimeUpdateState.ActiveMovedToBackup;
            await WriteJournalAsync(journalPath, journal, cancellationToken);

            Directory.Move(request.StagedRuntimeRoot, activeRoot);
            journal.State = RuntimeUpdateState.NewRuntimeActivated;
            await WriteJournalAsync(journalPath, journal, cancellationToken);

            await integrityVerifier.VerifyAsync(activeRoot, manifest, cancellationToken);

            return new RuntimeUpdateResult(
                activeRoot,
                RuntimePathPolicy.ResolveContainedFile(activeRoot, manifest.EntryPoint),
                backupRoot,
                manifest.Version);
        }
        catch
        {
            // Once activation starts, rollback must not be cancelled with the
            // operation that failed. Restoring the known-good runtime is the
            // final safety boundary.
            await RollBackAsync(activeRoot, backupRoot, CancellationToken.None);
            throw;
        }
    }

    public async Task RecoverInterruptedUpdateAsync(
        string cartRoot,
        CancellationToken cancellationToken = default)
    {
        var maintenanceRoot = Path.Combine(Path.GetFullPath(cartRoot), ".cartlaunch");
        var journalPath = Path.Combine(maintenanceRoot, "update-journal.json");
        if (!File.Exists(journalPath))
        {
            return;
        }

        RuntimeUpdateJournal? journal;
        await using (var stream = File.OpenRead(journalPath))
        {
            journal = await JsonSerializer.DeserializeAsync(
                stream,
                JournalJsonContext.RuntimeUpdateJournal,
                cancellationToken);
        }

        if (journal is null || journal.FormatVersion != 1 ||
            journal.Platform is not ("Windows-x64" or "Linux-x64"))
        {
            throw new InvalidDataException("The update recovery journal is invalid.");
        }

        var activeRoot = Path.Combine(cartRoot, "System", journal.Platform);
        var backupRoot = Path.Combine(maintenanceRoot, "previous-runtime", journal.Platform);

        if (journal.State == RuntimeUpdateState.Prepared)
        {
            // The directory move can reach disk before the following journal
            // update. A backup therefore takes precedence over the older state.
            if (Directory.Exists(backupRoot))
            {
                await RollBackAsync(activeRoot, backupRoot, CancellationToken.None);
            }

            File.Delete(journalPath);
            return;
        }

        if (journal.State is RuntimeUpdateState.ActiveMovedToBackup or
            RuntimeUpdateState.NewRuntimeActivated or RuntimeUpdateState.Restarted)
        {
            if (!Directory.Exists(backupRoot))
            {
                throw new InvalidDataException(
                    "The interrupted update cannot be recovered because its previous runtime is missing.");
            }

            await RollBackAsync(activeRoot, backupRoot, CancellationToken.None);
            File.Delete(journalPath);
            return;
        }

        throw new InvalidDataException("The update recovery journal state is invalid.");
    }

    public static void CompleteSuccessfulUpdate(string cartRoot, string platform)
    {
        var maintenanceRoot = Path.Combine(Path.GetFullPath(cartRoot), ".cartlaunch");
        var backupRoot = Path.Combine(maintenanceRoot, "previous-runtime", platform);
        var journalPath = Path.Combine(maintenanceRoot, "update-journal.json");

        if (Directory.Exists(backupRoot))
        {
            Directory.Delete(backupRoot, recursive: true);
        }

        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }
    }

    public static void RollBackActivatedUpdate(string cartRoot, string platform)
    {
        var fullCartRoot = Path.GetFullPath(cartRoot);
        var activeRoot = Path.Combine(fullCartRoot, "System", platform);
        var maintenanceRoot = Path.Combine(fullCartRoot, ".cartlaunch");
        var backupRoot = Path.Combine(maintenanceRoot, "previous-runtime", platform);
        var failedRoot = Path.Combine(maintenanceRoot, "failed-runtime", platform);

        if (!Directory.Exists(backupRoot))
        {
            throw new InvalidOperationException("No previous runtime is available for rollback.");
        }

        if (Directory.Exists(failedRoot))
        {
            Directory.Delete(failedRoot, recursive: true);
        }

        if (Directory.Exists(activeRoot))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(failedRoot)!);
            Directory.Move(activeRoot, failedRoot);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(activeRoot)!);
        Directory.Move(backupRoot, activeRoot);

        var journalPath = Path.Combine(maintenanceRoot, "update-journal.json");
        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }
    }

    private static void ValidateRequestPaths(RuntimeUpdateRequest request)
    {
        if (request.Platform is not ("Windows-x64" or "Linux-x64"))
        {
            throw new InvalidDataException("The update platform is unsupported.");
        }

        var cartRoot = Path.GetFullPath(request.CartRoot);
        var expectedStaging = Path.Combine(cartRoot, ".cartlaunch", "update-staging");
        if (!RuntimePathPolicy.IsContainedDirectory(expectedStaging, request.StagedRuntimeRoot) ||
            !RuntimePathPolicy.IsContainedDirectory(expectedStaging, request.ManifestPath))
        {
            throw new InvalidDataException("Update staging must remain inside the cart maintenance folder.");
        }

        var activeRoot = Path.Combine(cartRoot, "System", request.Platform);
        if (string.Equals(
                Path.GetFullPath(activeRoot),
                Path.GetFullPath(request.StagedRuntimeRoot),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("The active runtime cannot be used as update staging.");
        }
    }

    private static async Task WriteJournalAsync(
        string path,
        RuntimeUpdateJournal journal,
        CancellationToken cancellationToken)
    {
        journal.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         16 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                journal,
                JournalJsonContext.RuntimeUpdateJournal,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static Task RollBackAsync(
        string activeRoot,
        string backupRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(backupRoot))
        {
            return Task.CompletedTask;
        }

        if (Directory.Exists(activeRoot))
        {
            Directory.Delete(activeRoot, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(activeRoot)!);
        Directory.Move(backupRoot, activeRoot);

        return Task.CompletedTask;
    }

    private static string TryReadAssemblyVersion(string runtimeRoot, string entryPoint)
    {
        var path = Path.Combine(runtimeRoot, entryPoint);
        return File.Exists(path)
            ? System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion ?? ""
            : "";
    }
}
