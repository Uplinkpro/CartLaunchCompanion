namespace CartLaunchCompanion.Desktop.ViewModels;

public sealed class ScreenshotIndicatorViewModel : ViewModelBase
{
    private bool _isActive;

    public ScreenshotIndicatorViewModel(string accentColor, bool isActive)
    {
        AccentColor = accentColor;
        _isActive = isActive;
    }

    public string AccentColor { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
                OnPropertyChanged(nameof(Fill));
        }
    }

    public string Fill => IsActive ? AccentColor : "#70FFFFFF";
}
