using System.Text;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record WindowsDriveBrandingResult(bool Applied, string Detail);

public sealed class WindowsDriveBrandingService
{
    public const string AutorunFileName = "autorun.inf";
    public const string IconRelativePath = @"Cart\System\Assets\AppIcon.ico";

    public WindowsDriveBrandingResult Apply(string mediaRoot, string displayName)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        var icon = GetIconPath(root);
        if (!File.Exists(icon))
            return new(false, $"Drive icon not configured because {IconRelativePath} is missing.");

        RejectLink(new FileInfo(icon));
        var autorun = Path.Combine(root, AutorunFileName);
        var existing = new FileInfo(autorun);
        if (existing.Exists)
        {
            RejectLink(existing);
            if (OperatingSystem.IsWindows())
                File.SetAttributes(autorun, FileAttributes.Normal);
        }

        var label = SanitizeLabel(displayName);
        var content = $"[Autorun]\r\nIcon={IconRelativePath}\r\nLabel={label}\r\n";
        var temporary = autorun + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content, Encoding.ASCII);
            File.Move(temporary, autorun, overwrite: true);
            if (OperatingSystem.IsWindows())
                File.SetAttributes(autorun, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return new(true, $"Uses {IconRelativePath} with the label {label}. Reinsert the cart if Explorer has cached its old icon.");
    }

    public WindowsDriveBrandingResult Inspect(string mediaRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        var icon = new FileInfo(GetIconPath(root));
        var autorun = new FileInfo(Path.Combine(root, AutorunFileName));
        if (!icon.Exists || !autorun.Exists)
            return new(false, "Run Prepare physical cart to add the CLC drive icon and label.");
        RejectLink(icon);
        RejectLink(autorun);
        var content = File.ReadAllText(autorun.FullName);
        var expectedIcon = $"Icon={IconRelativePath}";
        var safe = content.Contains(expectedIcon, StringComparison.OrdinalIgnoreCase) &&
                   content.Contains("Label=", StringComparison.OrdinalIgnoreCase) &&
                   !content.Contains("Open=", StringComparison.OrdinalIgnoreCase) &&
                   !content.Contains("ShellExecute=", StringComparison.OrdinalIgnoreCase) &&
                   !content.Contains("[Shell", StringComparison.OrdinalIgnoreCase);
        return safe
            ? new(true, $"Windows drive branding points to {IconRelativePath}.")
            : new(false, "The root autorun.inf is missing the CLC icon reference or contains executable AutoRun directives.");
    }

    private static string SanitizeLabel(string value)
    {
        var printableAscii = new string((value ?? "Cart Launch Companion")
            .Trim()
            .Select(character => character is >= ' ' and <= '~' ? character : '-')
            .ToArray());
        if (string.IsNullOrWhiteSpace(printableAscii)) printableAscii = "Cart Launch Companion";
        return printableAscii.Length <= 32 ? printableAscii : printableAscii[..32].TrimEnd();
    }

    private static string GetIconPath(string root) =>
        Path.Combine(root, "Cart", "System", "Assets", "AppIcon.ico");

    private static void RejectLink(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new InvalidDataException("Links are not allowed in Windows drive branding paths.");
    }
}
