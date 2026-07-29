using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CartLaunchCompanion.Desktop.Input;
using CartLaunchCompanion.Core.Configuration.Migration;
using CartLaunchCompanion.Core.Configuration.Validation;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Library;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;
using CartLaunchCompanion.Desktop.ViewModels;
using CartLaunchCompanion.Desktop.Views;
using CartLaunchCompanion.Platform.Linux;
using CartLaunchCompanion.Platform.Windows;

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

            IGameLaunchService launchService =
                platformService.Current switch
                {
                    PlatformKind.Windows =>
                        new WindowsGameLaunchService(),

                    PlatformKind.Linux =>
                        new LinuxGameLaunchService(),

                    _ => new UnsupportedGameLaunchService()
                };

            MainWindow? mainWindow = null;

            var viewModel = new MainViewModel(
                libraryService,
                launchService,
                portablePaths,
                platformService.Current,
                () => desktop.Shutdown(),
                visible =>
                {
                    if (mainWindow is null)
                        return;

                    if (visible)
                    {
                        mainWindow.Show();
                        mainWindow.Activate();
                    }
                    else
                    {
                        mainWindow.Hide();
                    }
                });

            mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            var controllerService = new SdlControllerService();

            controllerService.InputReceived += async (_, input) =>
                await viewModel.HandleInputAsync(input);

            controllerService.ConnectionChanged += (_, connection) =>
                viewModel.UpdateControllerConnection(
                    connection.Connected,
                    connection.Description);

            controllerService.DiagnosticChanged += (_, diagnostic) =>
                viewModel.UpdateControllerDiagnostic(diagnostic);

            desktop.Exit += async (_, _) =>
                await controllerService.DisposeAsync();

            controllerService.Start();

            desktop.MainWindow = mainWindow;

            base.OnFrameworkInitializationCompleted();

            await viewModel.LoadAsync();
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private sealed class UnsupportedGameLaunchService
        : IGameLaunchService
    {
        public Task<GameLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                GameLaunchResult.Failure(
                    "The current operating system is unsupported."));
    }
}
