using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Portable;

public static class LauncherAssetCatalog
{
    private static readonly IReadOnlyDictionary<string, LauncherKind> FolderKinds =
        new Dictionary<string, LauncherKind>(StringComparer.Ordinal)
        {
            ["amazon"] = LauncherKind.Amazon,
            ["battlenet"] = LauncherKind.BattleNet,
            ["directexe"] = LauncherKind.Local,
            ["ea"] = LauncherKind.EA,
            ["epic"] = LauncherKind.Epic,
            ["flash"] = LauncherKind.Flash,
            ["gog"] = LauncherKind.GOG,
            ["hoyoverse"] = LauncherKind.HoYoverse,
            ["itchi"] = LauncherKind.ItchIo,
            ["itchio"] = LauncherKind.ItchIo,
            ["rockstar"] = LauncherKind.Rockstar,
            ["steam"] = LauncherKind.Steam,
            ["ubisoft"] = LauncherKind.Ubisoft,
            ["xbox"] = LauncherKind.Xbox
        };

    private static readonly LauncherKind[] DisplayOrder =
    [
        LauncherKind.Steam,
        LauncherKind.Xbox,
        LauncherKind.Epic,
        LauncherKind.GOG,
        LauncherKind.Ubisoft,
        LauncherKind.Rockstar,
        LauncherKind.Amazon,
        LauncherKind.EA,
        LauncherKind.BattleNet,
        LauncherKind.HoYoverse,
        LauncherKind.ItchIo,
        LauncherKind.Flash,
        LauncherKind.Local,
        LauncherKind.Custom
    ];

    public static IReadOnlyList<LauncherKind> GetAvailableWindowsLaunchers(params string[] assetRoots)
    {
        var found = new HashSet<LauncherKind>();
        foreach (var assetsRoot in assetRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var launchersRoot = Path.Combine(assetsRoot, "Launchers");
            if (!Directory.Exists(launchersRoot)) continue;

            string[] directories;
            try { directories = Directory.GetDirectories(launchersRoot, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }

            foreach (var directory in directories)
                if (FolderKinds.TryGetValue(Normalize(Path.GetFileName(directory)), out var kind))
                    found.Add(kind);
        }

        // Emulator branding is selected from the platform folders instead of a
        // generic launcher folder, so it remains a valid choice in every cart.
        found.Add(LauncherKind.Custom);
        return DisplayOrder.Where(found.Contains).ToArray();
    }

    public static string FolderName(LauncherKind launcher) => launcher switch
    {
        LauncherKind.Local => "DirectExe",
        LauncherKind.BattleNet => "battlenet",
        LauncherKind.HoYoverse => "Hoyoverse",
        LauncherKind.ItchIo => "itchi",
        _ => launcher.ToString()
    };

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
