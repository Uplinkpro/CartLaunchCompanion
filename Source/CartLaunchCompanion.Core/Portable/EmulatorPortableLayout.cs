using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Core.Portable;

public static class EmulatorPortableLayout
{
    public static readonly string[] SharedFolders = ["BIOS", "Saves", "States", "Screenshots", "Cheats"];

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
        foreach (var folder in SharedFolders) Directory.CreateDirectory(Path.Combine(sharedRoot, folder));
        WriteGuide(Path.Combine(sharedRoot, "ABOUT SHARED DATA.txt"),
            "Configure Windows and Linux builds to use these real shared folders.\nDo not use symlinks; they are unreliable across operating systems and removable-drive filesystems.\n");
        var romRoot = Directory.CreateDirectory(Path.Combine(mediaRoot, "Roms")).FullName;
        foreach (var folder in EmulatorLaunchPresetCatalog.All.SelectMany(item => item.RomFolders).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name))
            Directory.CreateDirectory(Path.Combine(romRoot, folder));
        WriteGuide(Path.Combine(romRoot, "PLACE ROMS HERE.txt"), "Place legally obtained game images in the matching platform folder.\n");
    }

    private static void WriteGuide(string path, string content)
    {
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }
}
