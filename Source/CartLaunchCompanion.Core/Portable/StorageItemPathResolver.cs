namespace CartLaunchCompanion.Core.Portable;

public static class StorageItemPathResolver
{
    public static string Resolve(Uri storagePath)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        if (storagePath.IsAbsoluteUri && storagePath.IsFile)
            return Path.GetFullPath(storagePath.LocalPath);

        var rawPath = Uri.UnescapeDataString(storagePath.OriginalString);
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new InvalidDataException("The selected storage item did not provide a usable local path.");

        // Windows drive roots can be returned by the native folder picker as
        // relative URIs (for example, H:\) even though they are absolute paths.
        if (!storagePath.IsAbsoluteUri || LooksLikeWindowsDrivePath(rawPath))
            return Path.GetFullPath(rawPath);

        throw new InvalidDataException($"The selected storage item is not a local file-system path: {storagePath.Scheme}.");
    }

    private static bool LooksLikeWindowsDrivePath(string value) =>
        value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' &&
        (value[2] == '\\' || value[2] == '/');
}
