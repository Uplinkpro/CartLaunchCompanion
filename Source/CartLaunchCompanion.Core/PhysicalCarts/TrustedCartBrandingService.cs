using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed class TrustedCartBrandingService
{
    private const long MaximumLogoBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    public async Task<string?> CacheCollectionLogoAsync(
        string mediaRoot,
        string cartId,
        string hostDataDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateCartId(cartId);
        var cartRoot = Path.Combine(Path.GetFullPath(mediaRoot), "Cart");
        var collection = await CollectionConfigurationJson.LoadAsync(
            Path.Combine(cartRoot, "Config"), cancellationToken);
        if (string.IsNullOrWhiteSpace(collection.Logo)) return null;

        var source = RuntimePathPolicy.ResolveContainedFile(cartRoot, collection.Logo);
        var sourceInfo = new FileInfo(source);
        var extension = Path.GetExtension(source);
        if (!sourceInfo.Exists || sourceInfo.Length is <= 0 or > MaximumLogoBytes ||
            !SupportedExtensions.Contains(extension))
            throw new InvalidDataException("The collection logo is missing, too large, or uses an unsupported image format.");

        var directory = GetBrandingDirectory(hostDataDirectory, cartId);
        Directory.CreateDirectory(directory);
        RejectLink(new DirectoryInfo(directory));
        var destination = Path.Combine(directory, "CollectionLogo" + extension.ToLowerInvariant());
        var temporary = destination + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            if (input.Length is <= 0 or > MaximumLogoBytes)
                throw new InvalidDataException("The collection logo changed while it was being approved.");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await input.CopyToAsync(output, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
            DeleteOtherLogoFormats(directory, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public string? GetCachedLogoPath(string hostDataDirectory, string cartId)
    {
        ValidateCartId(cartId);
        var directory = GetBrandingDirectory(hostDataDirectory, cartId);
        if (!Directory.Exists(directory)) return null;
        RejectLink(new DirectoryInfo(directory));
        foreach (var extension in SupportedExtensions)
        {
            var path = Path.Combine(directory, "CollectionLogo" + extension);
            var info = new FileInfo(path);
            if (!info.Exists) continue;
            RejectLink(info);
            return info.Length is > 0 and <= MaximumLogoBytes ? path : null;
        }
        return null;
    }

    public void RemoveCachedBranding(string hostDataDirectory, string cartId)
    {
        ValidateCartId(cartId);
        var directory = GetBrandingDirectory(hostDataDirectory, cartId);
        if (!Directory.Exists(directory)) return;
        RejectLink(new DirectoryInfo(directory));
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var info = new FileInfo(file);
            RejectLink(info);
            info.Delete();
        }
        Directory.Delete(directory, recursive: false);
    }

    private static string GetBrandingDirectory(string hostDataDirectory, string cartId)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(hostDataDirectory));
        var directory = Path.GetFullPath(Path.Combine(root, "Branding", cartId));
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The trusted branding path escapes the CLC-Cart Monitor data folder.");
        return directory;
    }

    private static void DeleteOtherLogoFormats(string directory, string retained)
    {
        foreach (var extension in SupportedExtensions)
        {
            var path = Path.Combine(directory, "CollectionLogo" + extension);
            if (!path.Equals(retained, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateCartId(string cartId)
    {
        if (!Guid.TryParseExact(cartId, "D", out _)) throw new InvalidDataException("The cart ID is invalid.");
    }

    private static void RejectLink(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new InvalidDataException("Links are not allowed in trusted cart branding.");
    }
}
