using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CartLaunchCompanion.Desktop.Controls;

public partial class ButtonPromptBadge : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ButtonPromptBadge, string>(nameof(Label), "A");

    public static readonly StyledProperty<IBrush> BadgeColorProperty =
        AvaloniaProperty.Register<ButtonPromptBadge, IBrush>(nameof(BadgeColor), new SolidColorBrush(Color.Parse("#2E9B32")));

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IBrush BadgeColor
    {
        get => GetValue(BadgeColorProperty);
        set => SetValue(BadgeColorProperty, value);
    }

    public ButtonPromptBadge() => InitializeComponent();
}
