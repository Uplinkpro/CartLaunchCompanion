using System.Security.Cryptography;
using System.Text;

namespace CartLaunchCompanion.Core.Updating;

public sealed class RuntimeIntegrityVerifier
{
    public const int MaximumFiles = 4096;

    public async Task VerifyAsync(
        string payloadRoot,
        RuntimeUpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadRoot);
        ArgumentNullException.ThrowIfNull(manifest);

        ValidateManifest(manifest);
        RejectReparsePoints(payloadRoot);

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalized = file.Path.Replace('\\', '/');
            if (!expected.Add(normalized))
            {
                throw new InvalidDataException($"Duplicate update path: '{file.Path}'.");
            }

            var path = RuntimePathPolicy.ResolveContainedFile(payloadRoot, normalized);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Length)
            {
                throw new InvalidDataException($"Update file length mismatch: '{file.Path}'.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            var actual = Convert.ToHexStringLower(hash);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actual),
                    Encoding.ASCII.GetBytes(file.Sha256.ToLowerInvariant())))
            {
                throw new InvalidDataException($"Update file hash mismatch: '{file.Path}'.");
            }
        }

        var actualFiles = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(payloadRoot, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        if (!actualFiles.SetEquals(expected))
        {
            var unexpected = actualFiles.Except(expected, StringComparer.Ordinal).FirstOrDefault();
            var missing = expected.Except(actualFiles, StringComparer.Ordinal).FirstOrDefault();
            throw new InvalidDataException(
                unexpected is not null
                    ? $"Unexpected file in update payload: '{unexpected}'."
                    : $"Missing file in update payload: '{missing}'.");
        }

        var fingerprint = ComputeRootFingerprint(manifest.Files);
        if (!string.Equals(fingerprint, manifest.RootFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update root fingerprint does not match its file manifest.");
        }
    }

    public static string ComputeRootFingerprint(IEnumerable<RuntimeUpdateFile> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            builder.Append(file.Path.Replace('\\', '/'))
                .Append('\0')
                .Append(file.Length)
                .Append('\0')
                .Append(file.Sha256.ToLowerInvariant())
                .Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void RejectReparsePoints(string payloadRoot)
    {
        var root = new DirectoryInfo(payloadRoot);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException("The staged update payload does not exist.");
        }

        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The staged update root cannot be a symbolic link or reparse point.");
        }

        foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Update payload links are not allowed: '{Path.GetRelativePath(payloadRoot, entry.FullName)}'.");
            }
        }
    }

    private static void ValidateManifest(RuntimeUpdateManifest manifest)
    {
        if (manifest.FormatVersion != 1 || manifest.SecurityVersion < 1)
        {
            throw new InvalidDataException("The update manifest version is unsupported.");
        }

        if (!string.Equals(manifest.Product, "Cart Launch Companion", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64 ||
            manifest.Platform is not ("Windows-x64" or "Linux-x64") ||
            manifest.Files.Count is 0 or > MaximumFiles)
        {
            throw new InvalidDataException("The update manifest contains invalid product information.");
        }

        var entryPoint = manifest.EntryPoint.Replace('\\', '/');
        var expectedEntryPoint = manifest.Platform == "Windows-x64"
            ? "CartLaunchCompanion.Desktop.exe"
            : "CartLaunchCompanion.Desktop";
        if (!string.Equals(entryPoint, expectedEntryPoint, StringComparison.Ordinal) ||
            !manifest.Files.Any(file => string.Equals(
                file.Path.Replace('\\', '/'),
                expectedEntryPoint,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The update entry point is invalid.");
        }

        foreach (var file in manifest.Files)
        {
            if (file.Length < 0 ||
                file.Path.Length is 0 or > 512 ||
                file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"Invalid update file record: '{file.Path}'.");
            }
        }
    }
}
