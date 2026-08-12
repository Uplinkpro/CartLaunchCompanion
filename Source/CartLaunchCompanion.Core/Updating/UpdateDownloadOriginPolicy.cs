namespace CartLaunchCompanion.Core.Updating;

public static class UpdateDownloadOriginPolicy
{
    private static readonly HashSet<string> ApprovedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };

    public static void Validate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !ApprovedHosts.Contains(uri.IdnHost))
        {
            throw new InvalidDataException(
                $"The update download origin is not approved: '{uri.GetLeftPart(UriPartial.Authority)}'.");
        }
    }
}
