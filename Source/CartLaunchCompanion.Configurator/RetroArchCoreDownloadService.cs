using System.IO.Compression;
using System.Runtime.InteropServices;

namespace CartLaunchCompanion.Configurator;

internal sealed class RetroArchCoreDownloadService(HttpClient? httpClient = null)
{
    private const long MaximumArchiveBytes = 64L * 1024 * 1024;
    private const long MaximumCoreBytes = 128L * 1024 * 1024;
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public static Uri GetDownloadUri(string coreName)
    {
        ValidateCoreName(coreName);
        var platform = OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsLinux() ? "linux" :
            throw new PlatformNotSupportedException("Automatic core downloads support Windows and Linux.");
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Automatic core downloads currently require an x64 system.");
        var binary = GetBinaryName(coreName);
        return new Uri($"https://buildbot.libretro.com/nightly/{platform}/x86_64/latest/{binary}.zip");
    }

    public static string GetBinaryName(string coreName)
    {
        ValidateCoreName(coreName);
        var extension = OperatingSystem.IsWindows() ? ".dll" :
            OperatingSystem.IsLinux() ? ".so" :
            throw new PlatformNotSupportedException("Automatic core downloads support Windows and Linux.");
        return $"{coreName}_libretro{extension}";
    }

    public async Task<string> DownloadAsync(
        string coreName,
        string coresFolder,
        CancellationToken cancellationToken = default)
    {
        var uri = GetDownloadUri(coreName);
        var expectedName = GetBinaryName(coreName);
        Directory.CreateDirectory(coresFolder);
        var destination = Path.GetFullPath(Path.Combine(coresFolder, expectedName));
        var containedRoot = Path.GetFullPath(coresFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(containedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The core destination escaped the portable RetroArch cores folder.");

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumArchiveBytes)
            throw new InvalidDataException("The core archive is larger than CLC's safety limit.");
        await using var network = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var archiveBuffer = new MemoryStream();
        await CopyWithLimitAsync(network, archiveBuffer, MaximumArchiveBytes, cancellationToken);
        archiveBuffer.Position = 0;

        using var archive = new ZipArchive(archiveBuffer, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (entries.Length != 1 ||
            !entries[0].FullName.Equals(expectedName, StringComparison.OrdinalIgnoreCase) ||
            entries[0].Length > MaximumCoreBytes)
            throw new InvalidDataException("The official archive did not contain the expected single Libretro core binary.");

        var temporary = destination + ".download";
        try
        {
            await using (var source = entries[0].Open())
            await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                await CopyWithLimitAsync(source, target, MaximumCoreBytes, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("The downloaded core exceeded CLC's safety limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void ValidateCoreName(string coreName)
    {
        if (string.IsNullOrWhiteSpace(coreName) ||
            coreName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
            throw new ArgumentException("The Libretro core name is invalid.", nameof(coreName));
    }
}
