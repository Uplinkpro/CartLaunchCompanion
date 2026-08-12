using System.IO.Compression;
using System.Security.Cryptography;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.Tests;

public sealed class RuntimeArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CLC-ArchiveTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ZipExtractionAcceptsExactSignedContents()
    {
        var archive = CreateZip(("app.exe", "runtime"), ("data/config.json", "{}"));
        var manifest = CreateManifest(("app.exe", "runtime"), ("data/config.json", "{}"));
        var destination = Path.Combine(_root, "exact");

        RuntimeArchiveExtractor.ExtractZip(archive, destination, manifest);

        Assert.Equal("runtime", File.ReadAllText(Path.Combine(destination, "app.exe")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(destination, "data", "config.json")));
    }

    [Fact]
    public void ZipExtractionRejectsUnexpectedFile()
    {
        var archive = CreateZip(("app.exe", "runtime"), ("extra.exe", "bad"));
        var manifest = CreateManifest(("app.exe", "runtime"));

        var error = Assert.Throws<InvalidDataException>(() =>
            RuntimeArchiveExtractor.ExtractZip(archive, Path.Combine(_root, "unexpected"), manifest));

        Assert.Contains("Unexpected", error.Message);
    }

    [Fact]
    public void ZipExtractionRejectsDuplicatePath()
    {
        Directory.CreateDirectory(_root);
        var archivePath = Path.Combine(_root, "duplicate.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "app.exe", "runtime");
            WriteEntry(archive, "app.exe", "runtime");
        }
        var manifest = CreateManifest(("app.exe", "runtime"));

        var error = Assert.Throws<InvalidDataException>(() =>
            RuntimeArchiveExtractor.ExtractZip(archivePath, Path.Combine(_root, "duplicate"), manifest));

        Assert.Contains("Duplicate", error.Message);
    }

    [Fact]
    public void ZipExtractionRejectsLengthBeyondSignedValue()
    {
        var archive = CreateZip(("app.exe", "expanded payload"));
        var manifest = CreateManifest(("app.exe", "short"));

        var error = Assert.Throws<InvalidDataException>(() =>
            RuntimeArchiveExtractor.ExtractZip(archive, Path.Combine(_root, "oversize"), manifest));

        Assert.Contains("length mismatch", error.Message);
    }

    [Fact]
    public void ZipExtractionRejectsMissingFile()
    {
        var archive = CreateZip(("app.exe", "runtime"));
        var manifest = CreateManifest(("app.exe", "runtime"), ("needed.dll", "library"));

        var error = Assert.Throws<InvalidDataException>(() =>
            RuntimeArchiveExtractor.ExtractZip(archive, Path.Combine(_root, "missing"), manifest));

        Assert.Contains("Missing", error.Message);
    }

    [Fact]
    public void ExpansionValidationRejectsOversizedFile()
    {
        var manifest = new RuntimeUpdateManifest
        {
            Files =
            [
                new RuntimeUpdateFile
                {
                    Path = "huge.bin",
                    Length = RuntimeArchiveExtractor.MaximumFileBytes + 1,
                    Sha256 = new string('a', 64)
                }
            ]
        };

        Assert.Throws<InvalidDataException>(() =>
            RuntimeArchiveExtractor.ValidateExpansion(manifest));
    }

    [Fact]
    public void ExpansionValidationRejectsOversizedTotal()
    {
        var manifest = new RuntimeUpdateManifest
        {
            Files =
            [
                FileRecord("one.bin", RuntimeArchiveExtractor.MaximumFileBytes),
                FileRecord("two.bin", RuntimeArchiveExtractor.MaximumFileBytes),
                FileRecord("three.bin", 1)
            ]
        };

        Assert.Throws<InvalidDataException>(() =>
            RuntimeArchiveExtractor.ValidateExpansion(manifest));
    }

    [Fact]
    public void ExpansionValidationRejectsExcessiveFileCount()
    {
        var manifest = new RuntimeUpdateManifest
        {
            Files = Enumerable.Range(0, RuntimeIntegrityVerifier.MaximumFiles + 1)
                .Select(index => FileRecord($"files/{index:D4}.bin", 0))
                .ToList()
        };

        Assert.Throws<InvalidDataException>(() =>
            RuntimeArchiveExtractor.ValidateExpansion(manifest));
    }

    private string CreateZip(params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
            WriteEntry(archive, file.Path, file.Content);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private static RuntimeUpdateManifest CreateManifest(
        params (string Path, string Content)[] files) =>
        new()
        {
            Files = files.Select(file =>
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(file.Content);
                return new RuntimeUpdateFile
                {
                    Path = file.Path,
                    Length = bytes.Length,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes))
                };
            }).ToList()
        };

    private static RuntimeUpdateFile FileRecord(string path, long length) => new()
    {
        Path = path,
        Length = length,
        Sha256 = new string('a', 64)
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
