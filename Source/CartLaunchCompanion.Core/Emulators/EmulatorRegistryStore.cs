using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

/// <summary>
/// JSON storage scoped to a portable media root. Concurrent writers fail with IOException;
/// callers may retry. Read/modify/write operations never silently replace unreadable state.
/// </summary>
public sealed class EmulatorRegistryStore : IEmulatorRegistryStore
{
    private readonly string _mediaRoot;

    public EmulatorRegistryStore(string mediaRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaRoot);
        _mediaRoot = Path.GetFullPath(mediaRoot);
    }

    public async Task<EmulatorRegistry> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = EmulatorPathContract.Resolve(_mediaRoot, EmulatorPathContract.RegistryRelativePath);
        try
        {
            // Delete sharing allows a reader to retain the old complete snapshot during replacement.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, useAsync: true);
            using var reader = new StreamReader(stream);
            return EmulatorManagementJson.ReadRegistry(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (FileNotFoundException) { return EmulatorRegistry.Empty; }
        catch (DirectoryNotFoundException) { return EmulatorRegistry.Empty; }
    }

    public Task UpsertAsync(EmulatorInstallation installation, CancellationToken cancellationToken = default)
    {
        EmulatorManagementJson.ValidateInstallation(installation);
        return MutateAsync(items =>
        {
            items.RemoveAll(item => item.EmulatorId == installation.EmulatorId && item.Platform == installation.Platform);
            items.Add(installation);
        }, cancellationToken);
    }

    public Task RemoveAsync(string emulatorId, PlatformKind platform, CancellationToken cancellationToken = default)
    {
        EmulatorManagementJson.ValidateId(emulatorId);
        if (platform is not (PlatformKind.Windows or PlatformKind.Linux))
            throw new InvalidDataException("Only Windows and Linux installations are supported.");
        return MutateAsync(items => items.RemoveAll(item => item.EmulatorId == emulatorId && item.Platform == platform),
            cancellationToken);
    }

    private async Task MutateAsync(Action<List<EmulatorInstallation>> mutate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = EmulatorPathContract.Resolve(_mediaRoot, EmulatorPathContract.RegistryRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lockPath = EmulatorPathContract.Resolve(_mediaRoot, EmulatorPathContract.RegistryRelativePath + ".lock");
        // Keep the lock file after release: deleting it can let different writers lock different inodes.
        using var writerLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var registry = await LoadAsync(cancellationToken);
        var items = registry.Installations.ToList();
        mutate(items);
        var json = EmulatorManagementJson.WriteRegistry(registry with
        {
            Installations = items.OrderBy(item => item.EmulatorId, StringComparer.Ordinal)
                .ThenBy(item => item.Platform).ToArray()
        });
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, useAsync: true))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            EmulatorPathContract.Resolve(_mediaRoot, EmulatorPathContract.RegistryRelativePath);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
