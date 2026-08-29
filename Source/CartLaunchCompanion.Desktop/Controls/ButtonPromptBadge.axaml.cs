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

    public static readonly DirectProperty<ButtonPromptBadge, bool> IsKeyboardPromptProperty =
        AvaloniaProperty.RegisterDirect<ButtonPromptBadge, bool>(nameof(IsKeyboardPrompt), control => control.IsKeyboardPrompt);

    public static readonly DirectProperty<ButtonPromptBadge, string> KeyboardLabelProperty =
        AvaloniaProperty.RegisterDirect<ButtonPromptBadge, string>(nameof(KeyboardLabel), control => control.KeyboardLabel);

    private bool _isKeyboardPrompt;
    private string _keyboardLabel = "[Enter]";

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

    public bool IsKeyboardPrompt
    {
        get => _isKeyboardPrompt;
        private set => SetAndRaise(IsKeyboardPromptProperty, ref _isKeyboardPrompt, value);
    }

    public string KeyboardLabel
    {
        get => _keyboardLabel;
        private set => SetAndRaise(KeyboardLabelProperty, ref _keyboardLabel, value);
    }

    public ButtonPromptBadge()
    {
        InitializeComponent();
        UpdatePromptPresentation(Label);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
            UpdatePromptPresentation(change.GetNewValue<string>() ?? string.Empty);
    }

    private void UpdatePromptPresentation(string label)
    {
        var normalized = label.Trim().ToUpperInvariant();
        IsKeyboardPrompt = normalized is not ("A" or "B" or "X" or "Y");
        KeyboardLabel = normalized switch
        {
            "ENTER" => "[Enter]",
            "ESC" => "[Esc]",
            "SPACE" => "[Space]",
            "X / SPACE" => "[X / Space]",
            _ => $"[{label.Trim()}]"
        };
    }
}
