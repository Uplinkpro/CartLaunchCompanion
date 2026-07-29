using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CartLaunchCompanion.Core.Configuration.Migration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Desktop.ViewModels;
using CartLaunchCompanion.Desktop.Views;

namespace CartLaunchCompanion.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            var portablePathService = new PortablePathService();
            var portablePaths =
                portablePathService.Discover(AppContext.BaseDirectory);

            var platformService = new RuntimePlatformService();

            var libraryService = new GameLibraryService(
                new GameConfigurationValidator(),
                new Version1GameConfigurationImporter(),
                new GamePathResolver(),
                new LaunchTargetSelector());

            var viewModel = new MainViewModel(
                libraryService,
                portablePaths,
                platformService.Current,
                () => desktop.Shutdown());

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            base.OnFrameworkInitializationCompleted();

            await viewModel.LoadAsync();
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
