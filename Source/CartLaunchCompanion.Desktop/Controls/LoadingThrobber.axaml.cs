using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CartLaunchCompanion.Desktop.Controls;

public partial class LoadingThrobber : UserControl
{
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<LoadingThrobber, IBrush?>(
            nameof(Accent),
            Brushes.White);

    public LoadingThrobber()
    {
        InitializeComponent();
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }
}
