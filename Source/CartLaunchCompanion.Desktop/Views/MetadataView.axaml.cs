using Avalonia.Controls;
using Avalonia.Threading;

namespace CartLaunchCompanion.Desktop.Views;

public partial class MetadataView : UserControl
{
    public MetadataView()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && IsVisible)
                Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
        };
    }
}
