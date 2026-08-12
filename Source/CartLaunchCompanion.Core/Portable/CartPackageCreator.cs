namespace CartLaunchCompanion.Core.Portable;

public sealed record CartPackageOptions(
    string SourceCartRoot,
    string DestinationMediaRoot,
    bool IncludeGameConfigurations = true,
    bool IncludeArtwork = true);

public sealed record CartPackageResult(string CartRoot, int FilesCopied, long BytesCopied);

public sealed class CartPackageCreator
{
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".github", "bin", "obj", "artifacts", "Logs", "Cache", "Concepts" };

    public async Task<CartPackageResult> CreateAsync(
        CartPackageOptions options, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var source = Path.GetFullPath(options.SourceCartRoot);
        var media = Path.GetFullPath(options.DestinationMediaRoot);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The source Cart folder does not exist.");
        if (media.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The destination cannot be inside the source Cart folder.");
        Directory.CreateDirectory(media);
        var destination = Path.Combine(media, "Cart");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException("The destination already contains a non-empty Cart folder.");

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(path => ShouldInclude(source, path, options)).ToArray();
        var totalBytes = files.Sum(path => new FileInfo(path).Length);
        var staging = Path.Combine(media, ".cartlaunch-package-" + Guid.NewGuid().ToString("N"));
        var copied = 0L;
        try
        {
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, path);
                var target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, cancellationToken);
                copied += input.Length;
                progress?.Report(totalBytes == 0 ? 1 : copied / (double)totalBytes);
            }
            Directory.Move(staging, destination);
            foreach (var name in new[] { "Games", "Emulators", "Roms" })
                Directory.CreateDirectory(Path.Combine(media, name));
            return new CartPackageResult(destination, files.Length, copied);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static bool ShouldInclude(string root, string path, CartPackageOptions options)
    {
        var segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(ExcludedNames.Contains)) return false;
        if (!options.IncludeGameConfigurations && segments.FirstOrDefault()?.Equals("Games", StringComparison.OrdinalIgnoreCase) == true) return false;
        if (!options.IncludeArtwork && segments.FirstOrDefault()?.Equals("Assets", StringComparison.OrdinalIgnoreCase) == true) return false;
        return !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
               !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
               !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
               !path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);
    }
}
