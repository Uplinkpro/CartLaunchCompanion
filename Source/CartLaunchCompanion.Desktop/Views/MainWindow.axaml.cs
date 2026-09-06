using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Input;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Desktop.Input;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly HomeView _homeView = new();
    private readonly MetadataView _metadataView = new();
    private MainViewModel? _pageViewModel;

    public MainWindow()
    {
        InitializeComponent();

        MainPages.PageTransition =
            CartLaunchCompanion.Desktop.Controls.AnimationPreferenceParser
                .IsReducedMotionValue(
                    Environment.GetEnvironmentVariable("CLC_REDUCE_MOTION"))
                ? null
                : new CrossFade(TimeSpan.FromMilliseconds(350));

        DataContextChanged += OnDataContextChanged;

        AddHandler(
            KeyDownEvent,
            OnPreviewKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        if (OperatingSystem.IsWindows())
        {
            // Stay above the Windows taskbar only while CLC owns focus. This
            // preserves console-style fullscreen without trapping the user:
            // Alt+Tab or activating another window immediately lowers CLC.
            Activated += (_, _) => Topmost = true;
            Deactivated += (_, _) => Topmost = false;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_pageViewModel is not null)
            _pageViewModel.PropertyChanged -= OnPageViewModelPropertyChanged;

        _pageViewModel = DataContext as MainViewModel;
        _homeView.DataContext = _pageViewModel;
        _metadataView.DataContext = _pageViewModel;

        if (_pageViewModel is null)
            return;

        _pageViewModel.PropertyChanged += OnPageViewModelPropertyChanged;
        ShowPage(_pageViewModel.ActivePageIndex);
    }

    private void OnPageViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActivePageIndex) &&
            sender is MainViewModel viewModel)
            ShowPage(viewModel.ActivePageIndex);
    }

    private void ShowPage(int pageIndex)
    {
        MainPages.Content = pageIndex switch
        {
            2 => _metadataView,
            _ => _homeView
        };
    }

    private async void OnPreviewKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var input = AvaloniaInputMapper.Map(e);

        if (input.Action is LauncherAction.None)
            return;

        e.Handled = true;
        await viewModel.HandleInputAsync(input);
    }
}
