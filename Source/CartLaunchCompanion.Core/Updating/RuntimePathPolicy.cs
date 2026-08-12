namespace CartLaunchCompanion.Core.Updating;

public static class RuntimePathPolicy
{
    private static readonly char[] AdditionalInvalidCharacters = [':', '*', '?', '"', '<', '>', '|', '\0'];

    public static string ResolveContainedFile(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var normalized = ValidateRelativePath(relativePath);
        var segments = normalized.Split('/');

        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(rootPath, Path.Combine(segments)));
        var prefix = rootPath + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException($"Update path escapes its payload: '{relativePath}'.");
        }

        RejectLinks(rootPath, candidate);
        return candidate;
    }

    public static string ValidateRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("//", StringComparison.Ordinal) ||
            relativePath.StartsWith("\\\\", StringComparison.Ordinal) || relativePath.IndexOfAny(AdditionalInvalidCharacters) >= 0 ||
            relativePath.Any(char.IsControl))
            throw new InvalidDataException($"Unsafe update path: '{relativePath}'.");
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Length > 255))
            throw new InvalidDataException($"Unsafe update path: '{relativePath}'.");
        return normalized;
    }

    public static bool IsContainedDirectory(string parent, string candidate)
    {
        var parentPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var candidatePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return candidatePath.StartsWith(parentPath + Path.DirectorySeparatorChar, PathComparison);
    }

    private static void RejectLinks(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        var current = root;

        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Links are not allowed in update payloads: '{relative}'.");
            }
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
