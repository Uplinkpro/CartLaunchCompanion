using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CartLaunchCompanion.Desktop.Views;

public partial class MetadataView : UserControl
{
    private readonly DispatcherTimer _descriptionDelayTimer;
    private readonly DispatcherTimer _descriptionScrollTimer;

    public MetadataView()
    {
        InitializeComponent();

        _descriptionDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _descriptionDelayTimer.Tick += BeginDescriptionScroll;

        _descriptionScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _descriptionScrollTimer.Tick += AdvanceDescriptionScroll;

        PropertyChanged += (_, args) =>
        {
            if (args.Property != IsVisibleProperty)
                return;

            if (IsVisible)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Focus();
                    RestartDescriptionScroll();
                }, DispatcherPriority.Input);
            }
            else
            {
                StopDescriptionScroll();
            }
        };
    }

    private void RestartDescriptionScroll()
    {
        StopDescriptionScroll();
        DescriptionScroller.Offset = new Vector(0, 0);
        _descriptionDelayTimer.Start();
    }

    private void StopDescriptionScroll()
    {
        _descriptionDelayTimer.Stop();
        _descriptionScrollTimer.Stop();
    }

    private void BeginDescriptionScroll(object? sender, EventArgs e)
    {
        _descriptionDelayTimer.Stop();
        if (IsVisible && DescriptionScroller.Extent.Height > DescriptionScroller.Viewport.Height)
            _descriptionScrollTimer.Start();
    }

    private void AdvanceDescriptionScroll(object? sender, EventArgs e)
    {
        var maximum = Math.Max(0, DescriptionScroller.Extent.Height - DescriptionScroller.Viewport.Height);
        if (DescriptionScroller.Offset.Y >= maximum)
        {
            _descriptionScrollTimer.Stop();
            return;
        }

        DescriptionScroller.Offset = new Vector(
            DescriptionScroller.Offset.X,
            Math.Min(maximum, DescriptionScroller.Offset.Y + 0.35));
    }
}
