using Avalonia.Media.Imaging;

namespace CartLaunchCompanion.Desktop.ViewModels;

public sealed class RetroAchievementItemViewModel(
    string title,
    string description,
    int points,
    bool hardcore,
    Bitmap? badgeImage,
    string? detailText = null) : IDisposable
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public string PointsText { get; } = detailText ?? $"{points} POINTS";
    public string EarnedText { get; } = hardcore ? "HARDCORE" : "UNLOCKED";
    public Bitmap? BadgeImage { get; } = badgeImage;
    public bool HasBadge => BadgeImage is not null;

    public void Dispose() => BadgeImage?.Dispose();
}
