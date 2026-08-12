namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record CartHostInstallResult(int FilesCopied, CartHostInstallationPlan Plan);

public sealed class CartHostInstallationService
{
    public async Task<CartHostInstallResult> InstallFilesAsync(
        string publishedHostRoot, CartHostInstallationPlan plan,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(publishedHostRoot);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The published Cart Launch Host folder was not found.");
        var executable = Path.Combine(source, Path.GetFileName(plan.ExecutablePath));
        if (!File.Exists(executable)) throw new FileNotFoundException("The published Cart Launch Host executable was not found.", executable);
        if (Path.GetFullPath(plan.InstallDirectory).StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The host cannot install inside its source folder.");

        Directory.CreateDirectory(plan.InstallDirectory);
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
                throw new InvalidDataException("Links are not allowed in the published host runtime.");
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is ".git" or "bin" or "obj") ||
                file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(plan.InstallDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
            count++;
        }
        return new CartHostInstallResult(count, plan);
    }

    public void RemoveUserData(CartHostInstallationPlan plan, bool removeTrust, bool removeSettings, bool removeLogs)
    {
        // Protected runtime sessions are transient executable copies, not user data.
        // They must never survive Host removal regardless of retention choices.
        DeleteContainedDirectory(plan.DataDirectory, Path.Combine(plan.DataDirectory, "Sessions"));
        if (removeTrust) DeleteContainedFile(plan.DataDirectory, plan.TrustDatabasePath);
        if (removeSettings) DeleteContainedFile(plan.DataDirectory, plan.SettingsPath);
        if (removeLogs)
            DeleteContainedDirectory(plan.DataDirectory, plan.LogsDirectory);
    }

    private static void DeleteContainedFile(string root, string path)
    {
        EnsureContained(root, path);
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteContainedDirectory(string root, string path)
    {
        EnsureContained(root, path);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void EnsureContained(string root, string path)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The host data path escapes its installation folder.");
    }
}
