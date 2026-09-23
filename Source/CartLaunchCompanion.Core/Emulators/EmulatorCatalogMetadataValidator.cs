using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Emulators;

internal static class EmulatorCatalogMetadataValidator
{
    public static void Validate(EmulatorDefinition definition)
    {
        if (definition.Branding is { } branding)
        {
            ValidateArtwork(branding.IconRelativePath);
            ValidateArtwork(branding.LogoRelativePath);
            ValidateArtwork(branding.BannerRelativePath);
            if (branding.ThemeColor is { } color &&
                (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)))
                throw new InvalidDataException("Theme colors must use #RRGGBB.");
            if (branding.UsesOfficialBranding && definition.Attribution?.BrandingSourceUrl is null)
                throw new InvalidDataException("Official branding requires a source attribution URL.");
        }

        if (definition.Attribution is { } attribution)
        {
            ValidateUrl(attribution.WebsiteUrl);
            ValidateUrl(attribution.RepositoryUrl);
            ValidateUrl(attribution.LicenseUrl);
            ValidateUrl(attribution.BrandingSourceUrl);
            ValidateOptionalText(attribution.License);
            ValidateOptionalText(attribution.Credits);
        }

        foreach (var channel in definition.ReleaseChannels)
        {
            ValidateOptionalText(channel.Description);
            if (channel.SupportedPlatforms is null ||
                channel.SupportedPlatforms.Any(p => p is not (PlatformKind.Windows or PlatformKind.Linux)) ||
                channel.SupportedPlatforms.Distinct().Count() != channel.SupportedPlatforms.Count)
                throw new InvalidDataException("Supported platforms must be unique Windows/Linux values.");
            if (channel.Source is { } source)
            {
                if (channel.SupportedPlatforms.Count == 0)
                    throw new InvalidDataException("A release source requires explicit supported platforms.");
                ValidateSource(source);
            }
        }

        if (definition.DefaultChannelId is { } defaultId)
        {
            EmulatorManagementJson.ValidateId(defaultId);
            if (!definition.ReleaseChannels.Any(channel => channel.Id == defaultId))
                throw new InvalidDataException("The default channel must exist in the same catalog entry.");
        }
    }

    private static void ValidateArtwork(string? path)
    {
        if (path is null) return;
        EmulatorPathContract.ValidateRelativePath(path);
        if (!path.StartsWith("Assets/Emulators/", StringComparison.Ordinal) || path.Split('/').Length < 4 ||
            !new[] { ".png", ".svg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Artwork must be a PNG/SVG under Assets/Emulators/<Folder>/ relative to the catalog.");
    }

    private static void ValidateSource(EmulatorReleaseSource source)
    {
        if (!Enum.IsDefined(source.Kind) || !Enum.IsDefined(source.Prereleases))
            throw new InvalidDataException("Unknown release source kind or prerelease policy.");
        ValidateUrl(source.Url);
        if (source.Url is null)
            throw new InvalidDataException("Release sources require a URL.");
        ValidateOptionalText(source.Tag);
        ValidateOptionalText(source.TagPrefix);
        if (new[] { source.Tag, source.TagPrefix }.Any(tag => tag is not null &&
                tag.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))))
            throw new InvalidDataException("Release tag filters cannot contain whitespace or control characters.");
        if (source.Tag is not null && source.TagPrefix is not null)
            throw new InvalidDataException("Use either an exact release tag or a tag prefix.");
        if (source.Kind == EmulatorReleaseSourceKind.ProjectWebsite)
        {
            if (source.Tag is not null || source.TagPrefix is not null || source.Prereleases != EmulatorPrereleasePolicy.Any)
                throw new InvalidDataException("Website sources cannot contain GitHub release filters.");
            return;
        }

        var uri = new Uri(source.Url);
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            parts.Length != 2 || parts.Any(p => p.Length == 0 || p is "." or ".." ||
                p.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')) ||
            (source.Url.TrimEnd('/') != $"https://github.com/{string.Join("/", parts)}"))
            throw new InvalidDataException("GitHub sources require a canonical https://github.com/<owner>/<repository> URL.");
    }

    private static void ValidateUrl(string? value)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
            value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Metadata links must be absolute HTTPS URLs without embedded credentials.");
    }

    private static void ValidateOptionalText(string? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Unknown optional metadata should be null or omitted, not blank.");
    }
}
