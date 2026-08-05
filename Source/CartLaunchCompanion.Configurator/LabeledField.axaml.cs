using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CartLaunchCompanion.Configurator;

public sealed partial class LabeledField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<LabeledField, string>(nameof(Label), "");
    public static readonly StyledProperty<string> DescriptionProperty = AvaloniaProperty.Register<LabeledField, string>(nameof(Description), "");
    public static readonly StyledProperty<string> RequirementProperty = AvaloniaProperty.Register<LabeledField, string>(nameof(Requirement), "Optional");
    public static readonly StyledProperty<Control?> FieldContentProperty = AvaloniaProperty.Register<LabeledField, Control?>(nameof(FieldContent));
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Requirement { get => GetValue(RequirementProperty); set => SetValue(RequirementProperty, value); }
    public Control? FieldContent { get => GetValue(FieldContentProperty); set => SetValue(FieldContentProperty, value); }
    public string BadgeClass => Requirement == "Required" ? "required" : Requirement == "Advanced" ? "advanced" : "optional";
    public LabeledField() { InitializeComponent(); }
}
