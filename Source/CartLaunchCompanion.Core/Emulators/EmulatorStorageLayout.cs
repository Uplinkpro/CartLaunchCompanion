namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Separates CLC-owned state from root-level portable emulator content.</summary>
public sealed record EmulatorStorageLayout(string StateRoot, string MediaRoot)
{
    public static EmulatorStorageLayout FromApplicationRoot(string applicationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        var stateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationRoot));
        var parent = Directory.GetParent(stateRoot)?.FullName;
        var usesNestedCartLayout = parent is not null &&
            (string.Equals(Path.GetFileName(stateRoot), "Cart",
                 OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
             Directory.Exists(Path.Combine(parent, "Emulators")) ||
             Directory.Exists(Path.Combine(parent, "Roms")));
        return new(stateRoot, usesNestedCartLayout ? parent! : stateRoot);
    }
}
