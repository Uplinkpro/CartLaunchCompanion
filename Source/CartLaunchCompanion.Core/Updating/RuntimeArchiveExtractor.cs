using System.Formats.Tar;
using System.IO.Compression;

namespace CartLaunchCompanion.Core.Updating;

internal static class RuntimeArchiveExtractor
{
    internal const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    internal const long MaximumFileBytes = 1024L * 1024 * 1024;

    public static long ValidateExpansion(RuntimeUpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Files.Count is 0 or > RuntimeIntegrityVerifier.MaximumFiles)
            throw new InvalidDataException("The update archive file count is invalid.");

        long total = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            var path = RuntimePathPolicy.ValidateRelativePath(file.Path);
            if (!paths.Add(path))
                throw new InvalidDataException($"Duplicate update archive path: '{file.Path}'.");
            if (file.Length < 0 || file.Length > MaximumFileBytes)
                throw new InvalidDataException($"Update file exceeds its extraction limit: '{file.Path}'.");
            total = checked(total + file.Length);
            if (total > MaximumExpandedBytes)
                throw new InvalidDataException("The expanded update exceeds its size limit.");
        }

        return total;
    }

    public static void ExtractZip(string archivePath, string destination, RuntimeUpdateManifest manifest)
    {
        var expected = CreateExpectedFiles(manifest);
        using var archive = ZipFile.OpenRead(archivePath);
        var extracted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            var path = RuntimePathPolicy.ValidateRelativePath(entry.FullName);
            var expectedLength = ValidateEntry(path, entry.Length, expected, extracted);
            var output = RuntimePathPolicy.ResolveContainedFile(destination, path);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = entry.Open();
            WriteBounded(input, output, expectedLength);
        }

        EnsureComplete(expected, extracted);
    }

    public static void ExtractTarGzip(string archivePath, string destination, RuntimeUpdateManifest manifest)
    {
        var expected = CreateExpectedFiles(manifest);
        var extracted = new HashSet<string>(StringComparer.Ordinal);
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.Directory)
                continue;
            if (entry.EntryType is not TarEntryType.RegularFile || entry.DataStream is null)
                throw new InvalidDataException("The Linux update contains an unsupported link or entry type.");
            var path = RuntimePathPolicy.ValidateRelativePath(entry.Name.TrimStart('.', '/'));
            var expectedLength = ValidateEntry(path, entry.Length, expected, extracted);
            var output = RuntimePathPolicy.ResolveContainedFile(destination, path);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            WriteBounded(entry.DataStream, output, expectedLength);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(output, entry.Mode);
        }

        EnsureComplete(expected, extracted);
    }

    private static Dictionary<string, long> CreateExpectedFiles(RuntimeUpdateManifest manifest)
    {
        ValidateExpansion(manifest);
        return manifest.Files.ToDictionary(
            file => RuntimePathPolicy.ValidateRelativePath(file.Path),
            file => file.Length,
            StringComparer.Ordinal);
    }

    private static long ValidateEntry(
        string path,
        long archiveLength,
        IReadOnlyDictionary<string, long> expected,
        HashSet<string> extracted)
    {
        if (!expected.TryGetValue(path, out var expectedLength))
            throw new InvalidDataException($"Unexpected file in update archive: '{path}'.");
        if (!extracted.Add(path))
            throw new InvalidDataException($"Duplicate file in update archive: '{path}'.");
        if (archiveLength != expectedLength)
            throw new InvalidDataException($"Update archive file length mismatch: '{path}'.");
        return expectedLength;
    }

    private static void WriteBounded(Stream input, string outputPath, long expectedLength)
    {
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total = checked(total + read);
            if (total > expectedLength)
                throw new InvalidDataException($"Update archive expanded beyond its signed size: '{Path.GetFileName(outputPath)}'.");
            output.Write(buffer, 0, read);
        }
        if (total != expectedLength)
            throw new InvalidDataException($"Update archive ended before its signed size: '{Path.GetFileName(outputPath)}'.");
        output.Flush(flushToDisk: true);
    }

    private static void EnsureComplete(
        IReadOnlyDictionary<string, long> expected,
        HashSet<string> extracted)
    {
        if (extracted.Count != expected.Count)
        {
            var missing = expected.Keys.Except(extracted, StringComparer.Ordinal).First();
            throw new InvalidDataException($"Missing file in update archive: '{missing}'.");
        }
    }
}
