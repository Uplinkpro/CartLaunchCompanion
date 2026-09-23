using System.Security.Cryptography;

namespace CartLaunchCompanion.Core.Portable;

public sealed record SharedResourceLink(string LinkPath, string TargetPath, string DisplayName);
public sealed record SharedResourceMigration(string SourcePath, string TargetPath, string DisplayName);

public interface ISharedResourceLinkManager
{
    IReadOnlyList<string> Preview(IReadOnlyList<SharedResourceLink> links);
    Task EnsureAsync(IReadOnlyList<SharedResourceLink> links, CancellationToken token = default);
    IReadOnlyList<string> PreviewMigrations(IReadOnlyList<SharedResourceMigration> migrations);
    Task MigrateAsync(IReadOnlyList<SharedResourceMigration> migrations, CancellationToken token = default);
}

/// <summary>Creates relative directory links only after a reversible, conflict-checked migration.</summary>
public sealed class SharedResourceLinkManager(string mediaRoot, string stateRoot) : ISharedResourceLinkManager
{
    private readonly string _mediaRoot = Path.GetFullPath(mediaRoot);
    private readonly string _stateRoot = Path.GetFullPath(stateRoot);

    public IReadOnlyList<string> Preview(IReadOnlyList<SharedResourceLink> links) => links
        .Where(link => !IsCorrectLink(link))
        .Select(link => Directory.Exists(link.LinkPath) && new DirectoryInfo(link.LinkPath).LinkTarget is null
            ? $"Move existing {link.DisplayName} into shared storage and create a relative link"
            : $"Create the shared {link.DisplayName} relative link")
        .ToArray();

    public IReadOnlyList<string> PreviewMigrations(IReadOnlyList<SharedResourceMigration> migrations) => migrations
        .Where(migration => Directory.Exists(migration.SourcePath) &&
            Directory.EnumerateFileSystemEntries(migration.SourcePath).Any())
        .Select(migration => $"Move existing {migration.DisplayName} into shared storage")
        .ToArray();

    public async Task MigrateAsync(IReadOnlyList<SharedResourceMigration> migrations,
        CancellationToken token = default)
    {
        foreach (var migration in migrations)
        {
            ValidateWithinMedia(migration.SourcePath);
            ValidateWithinMedia(migration.TargetPath);
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(migration.SourcePath)) continue;
            var sourceInfo = new DirectoryInfo(migration.SourcePath);
            if (sourceInfo.LinkTarget is not null || (sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"The existing {migration.DisplayName} folder is already linked and cannot be migrated automatically.");
            Directory.CreateDirectory(migration.TargetPath);
            await PreflightCopyAsync(migration.SourcePath, migration.TargetPath, token);
            await CopyMissingAsync(migration.SourcePath, migration.TargetPath, token);
            if (Directory.EnumerateFileSystemEntries(migration.SourcePath).Any())
            {
                var backup = BackupPath(migration.SourcePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(migration.SourcePath, backup);
            }
            else Directory.Delete(migration.SourcePath);
        }
    }

    public async Task EnsureAsync(IReadOnlyList<SharedResourceLink> links, CancellationToken token = default)
    {
        if (links.Count == 0 || links.All(IsCorrectLink)) return;
        foreach (var link in links)
        {
            ValidateWithinMedia(link.LinkPath);
            ValidateWithinMedia(link.TargetPath);
        }
        ProbeRelativeLinkSupport();
        foreach (var link in links)
        {
            token.ThrowIfCancellationRequested();
            if (IsCorrectLink(link)) continue;
            await EnsureOneAsync(link, token);
        }
    }

    private async Task EnsureOneAsync(SharedResourceLink link, CancellationToken token)
    {
        var linkInfo = new DirectoryInfo(link.LinkPath);
        if (linkInfo.Exists && linkInfo.LinkTarget is not null)
            throw new IOException($"{link.DisplayName} already points somewhere else. Remove or repair that link before continuing.");

        Directory.CreateDirectory(link.TargetPath);
        string? backup = null;
        if (linkInfo.Exists)
        {
            await PreflightCopyAsync(link.LinkPath, link.TargetPath, token);
            await CopyMissingAsync(link.LinkPath, link.TargetPath, token);
            if (Directory.EnumerateFileSystemEntries(link.LinkPath).Any())
            {
                backup = BackupPath(link.LinkPath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(link.LinkPath, backup);
            }
            else Directory.Delete(link.LinkPath);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link.LinkPath)!);
            var relative = Path.GetRelativePath(Path.GetDirectoryName(link.LinkPath)!, link.TargetPath);
            Directory.CreateSymbolicLink(link.LinkPath, relative);
            VerifyLink(link);
        }
        catch
        {
            if (Directory.Exists(link.LinkPath) && new DirectoryInfo(link.LinkPath).LinkTarget is not null)
                Directory.Delete(link.LinkPath);
            if (backup is not null && Directory.Exists(backup) && !Directory.Exists(link.LinkPath))
                Directory.Move(backup, link.LinkPath);
            throw;
        }
    }

    private void ProbeRelativeLinkSupport()
    {
        var probeRoot = Path.Combine(_stateRoot, "Config", "EmulatorCompanion", "LinkProbe");
        var target = Path.Combine(probeRoot, "target-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(probeRoot, "link-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(link, Path.GetRelativePath(probeRoot, target));
            if (!Directory.Exists(link)) throw new IOException("The relative link could not be followed.");
        }
        catch (UnauthorizedAccessException error)
        {
            throw new NotSupportedException(
                "Windows could not create the shared-data links. Enable Developer Mode once or run Emulator Companion as administrator, then apply setup again.", error);
        }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xFFFF) == 1314)
        {
            throw new NotSupportedException(
                "Windows could not create the shared-data links. Enable Developer Mode once or run Emulator Companion as administrator, then apply setup again.", error);
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(target)) Directory.Delete(target);
        }
    }

    private static bool IsCorrectLink(SharedResourceLink link)
    {
        var info = new DirectoryInfo(link.LinkPath);
        if (!info.Exists || info.LinkTarget is null) return false;
        var resolved = Path.GetFullPath(Path.Combine(info.Parent!.FullName, info.LinkTarget));
        return string.Equals(resolved.TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(link.TargetPath).TrimEnd(Path.DirectorySeparatorChar), PathComparison());
    }

    private static void VerifyLink(SharedResourceLink link)
    {
        if (!IsCorrectLink(link) || !Directory.Exists(link.LinkPath))
            throw new IOException($"The {link.DisplayName} link could not be verified.");
    }

    private async Task PreflightCopyAsync(string source, string target, CancellationToken token)
    {
        var visited = 0;
        foreach (var file in SafeFiles(source))
        {
            token.ThrowIfCancellationRequested();
            if (++visited > 100000) throw new InvalidDataException("The shared-data migration contains too many files.");
            var relative = Path.GetRelativePath(source, file.FullName);
            var destination = Path.Combine(target, relative);
            if (File.Exists(destination) && !await FilesMatchAsync(file.FullName, destination, token))
                throw new IOException($"Shared storage already contains a different file at {relative}. Resolve that conflict before continuing.");
        }
    }

    private static async Task CopyMissingAsync(string source, string target, CancellationToken token)
    {
        foreach (var file in SafeFiles(source))
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetRelativePath(source, file.FullName));
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
            File.SetLastWriteTimeUtc(destination, file.LastWriteTimeUtc);
        }
    }

    private static IEnumerable<FileInfo> SafeFiles(string root)
    {
        foreach (var directory in new DirectoryInfo(root).EnumerateDirectories("*", SearchOption.AllDirectories))
            if (directory.LinkTarget is not null || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Existing linked folders cannot be migrated automatically.");
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Existing linked files cannot be migrated automatically.");
            yield return file;
        }
    }

    private string BackupPath(string sourcePath)
    {
        var relative = Path.GetRelativePath(_mediaRoot, sourcePath);
        return Path.Combine(_stateRoot, "Config", "EmulatorCompanion", "Backups", "SharedResources",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"), relative);
    }

    private void ValidateWithinMedia(string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = _mediaRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, PathComparison()))
            throw new InvalidDataException("Shared-resource paths must stay inside the cart media root.");
    }

    private static async Task<bool> FilesMatchAsync(string left, string right, CancellationToken token)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length) return false;
        await using var a = File.OpenRead(left);
        await using var b = File.OpenRead(right);
        var leftHash = await SHA256.HashDataAsync(a, token);
        var rightHash = await SHA256.HashDataAsync(b, token);
        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
