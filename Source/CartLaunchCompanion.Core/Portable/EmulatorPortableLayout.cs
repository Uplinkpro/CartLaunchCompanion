using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Core.Portable;

public static class EmulatorPortableLayout
{
    public static IReadOnlyList<string> SharedFolders => SharedEmulatorResourceLayout.ResourceFolders;

    public static void Create(string mediaRoot)
    {
        var emulatorRoot = Directory.CreateDirectory(Path.Combine(mediaRoot, "Emulators")).FullName;
        foreach (var platform in new[] { "Windows", "Linux" })
        {
            var platformRoot = Directory.CreateDirectory(Path.Combine(emulatorRoot, platform)).FullName;
            foreach (var emulator in EmulatorLaunchPresetCatalog.All)
                Directory.CreateDirectory(Path.Combine(platformRoot, emulator.FolderName));
            WriteGuide(Path.Combine(platformRoot, "PLACE EMULATORS HERE.txt"),
                $"Place portable {platform} emulator files in the matching folder.\n" +
                (platform == "Linux" ? "Use official x86_64 AppImages where available.\n" : "Use portable builds rather than installers where available.\n"));
        }
        var sharedRoot = Directory.CreateDirectory(Path.Combine(emulatorRoot, "Shared")).FullName;
        SharedEmulatorResourceLayout.Create(mediaRoot);
        WriteGuide(Path.Combine(sharedRoot, "ABOUT SHARED DATA.txt"),
            "Windows and Linux emulator builds use these real shared folders for BIOS, saves, states, screenshots, cheats, and texture packs.\n" +
            "Emulator Companion configures a relative path when supported and creates a repairable platform-specific relative link only when required.\n" +
            "The shared folders contain the real data; links are never the authoritative copy.\n");
        var romRoot = Directory.CreateDirectory(Path.Combine(mediaRoot, "Roms")).FullName;
        foreach (var folder in EmulatorLaunchPresetCatalog.All.SelectMany(item => item.RomFolders).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name))
        {
            Directory.CreateDirectory(Path.Combine(romRoot, folder));
            GameContentLayout.PreparePlatform(mediaRoot, folder);
        }
        WriteGuide(Path.Combine(romRoot, "PLACE ROMS HERE.txt"), "Place legally obtained game images in the matching platform folder.\n");
    }

    private static void WriteGuide(string path, string content)
    {
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }
}
