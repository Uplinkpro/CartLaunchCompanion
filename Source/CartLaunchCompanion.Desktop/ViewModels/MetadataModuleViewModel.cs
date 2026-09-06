namespace CartLaunchCompanion.Desktop.ViewModels;

public enum MetadataModuleKind
{
    Library,
    Platform,
    LastPlayed,
    TimePlayed,
    Achievements
}

public sealed class MetadataModuleViewModel(
    string key,
    string label,
    string value,
    MetadataModuleKind kind,
    int order)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Value { get; } = value;
    public MetadataModuleKind Kind { get; } = kind;
    public int Order { get; } = order;
    public bool IsLibrary => Kind == MetadataModuleKind.Library;
    public bool IsPlatform => Kind == MetadataModuleKind.Platform;
    public bool IsLastPlayed => Kind == MetadataModuleKind.LastPlayed;
    public bool IsTimePlayed => Kind == MetadataModuleKind.TimePlayed;
    public bool IsAchievements => Kind == MetadataModuleKind.Achievements;
}
