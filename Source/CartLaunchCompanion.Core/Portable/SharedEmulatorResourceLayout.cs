using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Core.Portable;

public static class SharedEmulatorResourceLayout
{
    public static readonly string[] ResourceFolders =
        ["BIOS", "Saves", "States", "Screenshots", "Cheats", "TexturePacks"];

    public static void Create(string mediaRoot)
    {
        var shared = Directory.CreateDirectory(Path.Combine(Path.GetFullPath(mediaRoot), "Emulators", "Shared")).FullName;
        foreach (var resource in ResourceFolders)
        {
            var resourceRoot = Directory.CreateDirectory(Path.Combine(shared, resource)).FullName;
            foreach (var emulator in EmulatorLaunchPresetCatalog.All)
                Directory.CreateDirectory(Path.Combine(resourceRoot, emulator.FolderName));
        }
    }

    public static void Prepare(string mediaRoot, string emulatorFolder)
    {
        ValidateSegment(emulatorFolder);
        foreach (var resource in ResourceFolders)
            Directory.CreateDirectory(Path.Combine(Path.GetFullPath(mediaRoot), "Emulators", "Shared", resource, emulatorFolder));
    }

    public static string GetPath(string mediaRoot, string resource, string emulatorFolder)
    {
        if (!ResourceFolders.Contains(resource, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unknown shared emulator resource.", nameof(resource));
        ValidateSegment(emulatorFolder);
        return Path.Combine(Path.GetFullPath(mediaRoot), "Emulators", "Shared", resource, emulatorFolder);
    }

    public static string RelativeFromEmulator(string resource, string emulatorFolder)
    {
        if (!ResourceFolders.Contains(resource, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unknown shared emulator resource.", nameof(resource));
        ValidateSegment(emulatorFolder);
        return $"../../Shared/{resource}/{emulatorFolder}";
    }

    private static void ValidateSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("The emulator folder must be one safe path segment.", nameof(value));
    }
}
