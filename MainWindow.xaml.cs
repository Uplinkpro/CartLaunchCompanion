using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CartLaunchCompanion.Models;
using CartLaunchCompanion.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using Windows.Graphics;
using Windows.Gaming.Input;
using Windows.System;
using Windows.Media.Core;
using Windows.Media.Playback;
using WinRT.Interop;
using LibVLCSharp.Shared;
using LibVLCSharp.Platforms.Windows;

namespace CartLaunchCompanion;

public sealed partial class MainWindow : Window
{
    private readonly Grid Root = new();
    private readonly Image BackgroundImage = new();
    private readonly Image LauncherBrandLogo = new();
    private readonly Image AppBrandIcon = new();
    private readonly TextBlock AppBrandTitle = new();
    private readonly TextBlock AppBrandSubtitle = new();
    private readonly Border LauncherLogoGlow = new();
    private readonly Border CollectionPromptBar = new();
    private readonly Grid ExitConfirmationOverlay = new();
    private readonly Grid CollectionPage = new();
    private readonly Button ExitCollectionButton = new();
    private readonly GridView GamesGrid = new();
    private readonly Grid DetailsPage = new();
    private readonly Button BackButton = new();
    private readonly Button ExitDetailsButton = new();
    private readonly Image DetailHeader = new();
    private readonly Image DetailCover = new();
    private readonly Image DetailLauncherLogo = new();
    private readonly Border DetailLauncherLogoGlow = new();
    private readonly TextBlock DetailTitle = new();
    private readonly TextBlock DetailMetadata = new();
    private readonly TextBlock DetailDescription = new();
    private readonly Grid TrailerNativeHost = new();
    private readonly Border TrailerStatus = new();
    private readonly TextBlock TrailerStatusText = new();
    private readonly Button TrailerFallback = new();
    private readonly Button LaunchButton = new();
    private readonly StackPanel GamepadPromptBar = new();
    private readonly ObservableCollection<GameDefinition> _games = [];
    private readonly GameLibraryService _library = new();
    private readonly SteamService _steam = new();
    private readonly ArtworkService _artwork = new();
    private readonly string _root = PortablePaths.RootDirectory;
    private GameDefinition? _currentGame;
    private IntPtr _vlcVideoHwnd;
    private WebView2? _youTubePlayer;
    private MediaPlayerElement? _windowsMediaView;
    private Windows.Media.Playback.MediaPlayer? _windowsMediaPlayer;
    private LibVLC? _libVlc;
    private LibVLCSharp.Shared.MediaPlayer? _vlcMediaPlayer;
    private Media? _vlcMedia;

    private bool _transitioning;
    private readonly DispatcherTimer _gamepadTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private GamepadButtons _previousGamepadButtons;
    private DateTime _lastGamepadMove = DateTime.MinValue;
    private bool _gamepadActionInProgress;
    private bool _isClosing;
    private bool _exitConfirmationVisible;

    public MainWindow()
    {
        Content = Root;
        BuildVisualTree();
        ConfigureWindow();
        ConfigureGameGridTemplates();
        GamesGrid.ItemsSource = _games;

        // LibVLC/DirectX initialization is deliberately deferred until a non-YouTube
        // trailer is actually requested. Creating VideoView during MainWindow startup
        // can terminate an unpackaged WinUI process before the first window appears.
        Root.Loaded += async (_, _) => await LoadLibraryAsync();
        Root.KeyDown += Root_KeyDown;
        _gamepadTimer.Tick += GamepadTimer_Tick;
        _gamepadTimer.Start();
        Closed += (_, _) =>
        {
            _isClosing = true;
            _gamepadTimer.Stop();
            StopTrailerPlayback();
            DestroyVlcVideoHost();
            _vlcMediaPlayer?.Dispose();
            _windowsMediaPlayer?.Dispose();
            _libVlc?.Dispose();
        };
    }


    private void ConfigureGameGridTemplates()
    {
        // The home screen is a curated cartridge shelf, not a large library browser.
        // A single horizontal row keeps up to ten games readable from couch distance.
        GamesGrid.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load(
            "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<ItemsWrapGrid Orientation='Horizontal' MaximumRowsOrColumns='1' HorizontalAlignment='Center' VerticalAlignment='Bottom'/>" +
            "</ItemsPanelTemplate>");

        GamesGrid.ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<Grid Margin='7,10,7,20'>" +
            "<Grid.RowDefinitions><RowDefinition Height='40'/><RowDefinition Height='3'/><RowDefinition Height='*'/></Grid.RowDefinitions>" +
            "<Border Grid.Row='0' Background='{Binding LauncherBannerBrush}' CornerRadius='3,3,0,0'>" +
            "<StackPanel Orientation='Horizontal' HorizontalAlignment='Center' VerticalAlignment='Center' Spacing='8'>" +
            "<Image Source='{Binding LauncherLogo}' Width='21' Height='21' Stretch='Uniform'/>" +
            "<TextBlock Text='{Binding LauncherDisplayName}' Foreground='White' FontSize='13' FontWeight='SemiBold' VerticalAlignment='Center'/>" +
            "</StackPanel>" +
            "</Border>" +
            "<Rectangle Grid.Row='1' Fill='White'/>" +
            "<Border Grid.Row='2' Background='#FF080A0D' CornerRadius='0,0,4,4' BorderBrush='#557D8490' BorderThickness='1,0,1,1'>" +
            "<Image Source='{Binding CoverImage}' Stretch='UniformToFill'/>" +
            "</Border>" +
            "</Grid>" +
            "</DataTemplate>");

        GamesGrid.HorizontalContentAlignment = HorizontalAlignment.Center;
        GamesGrid.VerticalContentAlignment = VerticalAlignment.Bottom;
    }

    private static void MakeSquare(Button button)
    {
        button.CornerRadius = new CornerRadius(0);
    }

    private static FrameworkElement CreateGamepadPrompt(string buttonText, string actionText)
    {
        var badge = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = new TextBlock
            {
                Text = buttonText,
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };

        var label = new TextBlock
        {
            Text = actionText,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var prompt = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        prompt.Children.Add(badge);
        prompt.Children.Add(label);
        return prompt;
    }

    private void BuildVisualTree()
    {
        Root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 9, 12, 16));

        BackgroundImage.Stretch = Stretch.UniformToFill;
        BackgroundImage.Opacity = 0.50;
        Root.Children.Add(BackgroundImage);

        Root.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(137, 0, 0, 0))
        });

        CollectionPage.HorizontalAlignment = HorizontalAlignment.Stretch;
        CollectionPage.VerticalAlignment = VerticalAlignment.Stretch;
        CollectionPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(280) });
        CollectionPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        CollectionPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        CollectionPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) });

        // Premium display-case lighting: a soft overhead beam and a restrained
        // graphite floor. Launcher identity is carried by the logo glow.
        var overheadLight = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 760,
            Height = 420,
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                Center = new Windows.Foundation.Point(0.5, 0),
                GradientOrigin = new Windows.Foundation.Point(0.5, 0),
                RadiusX = 0.58,
                RadiusY = 1.0,
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(100, 119, 70, 210), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(30, 67, 36, 120), Offset = 0.48 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = 1 }
                }
            }
        };
        Grid.SetRowSpan(overheadLight, 3);
        CollectionPage.Children.Add(overheadLight);

        var brandStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Margin = new Thickness(0, 26, 0, 0)
        };
        AppBrandIcon.Width = 100;
        AppBrandIcon.Height = 100;
        AppBrandIcon.Stretch = Stretch.Uniform;
        AppBrandIcon.Source = CreateBitmap(Path.Combine(_root, "Assets", "AppIcon.png"));
        AppBrandIcon.HorizontalAlignment = HorizontalAlignment.Center;
        brandStack.Children.Add(AppBrandIcon);

        AppBrandTitle.Text = "CART LAUNCH";
        AppBrandTitle.FontSize = 42;
        AppBrandTitle.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        AppBrandTitle.CharacterSpacing = 65;
        AppBrandTitle.Foreground = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Microsoft.UI.Colors.White, Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 161, 164, 174), Offset = 0.52 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 238, 239, 243), Offset = 1 }
            }
        };
        brandStack.Children.Add(AppBrandTitle);

        AppBrandSubtitle.Text = "COMPANION";
        AppBrandSubtitle.FontSize = 21;
        AppBrandSubtitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AppBrandSubtitle.CharacterSpacing = 430;
        AppBrandSubtitle.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 157, 86, 232));
        AppBrandSubtitle.HorizontalAlignment = HorizontalAlignment.Center;
        brandStack.Children.Add(AppBrandSubtitle);
        CollectionPage.Children.Add(brandStack);

        LauncherBrandLogo.HorizontalAlignment = HorizontalAlignment.Center;
        LauncherBrandLogo.VerticalAlignment = VerticalAlignment.Center;
        LauncherBrandLogo.Width = 92;
        LauncherBrandLogo.Height = 62;
        LauncherBrandLogo.Stretch = Stretch.Uniform;
        LauncherBrandLogo.Opacity = 0.96;
        LauncherLogoGlow.Width = 126;
        LauncherLogoGlow.Height = 78;
        LauncherLogoGlow.CornerRadius = new CornerRadius(39);
        LauncherLogoGlow.HorizontalAlignment = HorizontalAlignment.Center;
        LauncherLogoGlow.VerticalAlignment = VerticalAlignment.Center;
        LauncherLogoGlow.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 72, 139, 255));
        LauncherLogoGlow.Child = LauncherBrandLogo;
        Grid.SetRow(LauncherLogoGlow, 1);
        CollectionPage.Children.Add(LauncherLogoGlow);

        Grid.SetRow(GamesGrid, 2);
        GamesGrid.IsItemClickEnabled = true;
        GamesGrid.SelectionMode = ListViewSelectionMode.Single;
        GamesGrid.IsTabStop = true;
        GamesGrid.Padding = new Thickness(28, 0, 28, 2);
        GamesGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        GamesGrid.VerticalAlignment = VerticalAlignment.Stretch;
        GamesGrid.ItemClick += GamesGrid_ItemClick;
        GamesGrid.SelectionChanged += GamesGrid_SelectionChanged;
        GamesGrid.SizeChanged += GamesGrid_SizeChanged;
        CollectionPage.Children.Add(GamesGrid);

        var promptStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 62
        };
        promptStack.Children.Add(CreateGamepadPrompt("A", "VIEW DETAILS"));
        promptStack.Children.Add(CreateGamepadPrompt("B", "EXIT"));
        CollectionPromptBar.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(218, 5, 7, 10));
        CollectionPromptBar.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 116, 121, 132));
        CollectionPromptBar.BorderThickness = new Thickness(1);
        CollectionPromptBar.CornerRadius = new CornerRadius(10);
        CollectionPromptBar.Margin = new Thickness(140, 4, 140, 14);
        CollectionPromptBar.Child = promptStack;
        Grid.SetRow(CollectionPromptBar, 3);
        CollectionPage.Children.Add(CollectionPromptBar);

        Root.Children.Add(CollectionPage);

        ExitConfirmationOverlay.Visibility = Visibility.Collapsed;
        ExitConfirmationOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(188, 0, 0, 0));
        ExitConfirmationOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
        ExitConfirmationOverlay.VerticalAlignment = VerticalAlignment.Stretch;
        ExitConfirmationOverlay.IsHitTestVisible = true;

        var exitPanel = new Border
        {
            Width = 560,
            Padding = new Thickness(42, 34, 42, 32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(245, 10, 12, 17)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(210, 157, 86, 232)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12)
        };
        var exitStack = new StackPanel { Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center };
        exitStack.Children.Add(new TextBlock
        {
            Text = "EXIT CART LAUNCH COMPANION?",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });
        exitStack.Children.Add(new TextBlock
        {
            Text = "Press B again to exit",
            FontSize = 18,
            Opacity = 0.82,
            TextAlignment = TextAlignment.Center
        });
        exitStack.Children.Add(CreateGamepadPrompt("A", "CANCEL"));
        exitPanel.Child = exitStack;
        ExitConfirmationOverlay.Children.Add(exitPanel);
        Root.Children.Add(ExitConfirmationOverlay);
        DetailsPage.Visibility = Visibility.Collapsed;
        DetailsPage.Opacity = 0;
        DetailsPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.39, GridUnitType.Star) });
        DetailsPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.61, GridUnitType.Star) });
        DetailsPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) });

        // Cinematic hero artwork. Header assets are authored at 3840x1240 and
        // cropped responsively so they retain the same visual weight at 720p–4K.
        var heroGrid = new Grid();
        Grid.SetRow(heroGrid, 0);
        DetailHeader.Stretch = Stretch.UniformToFill;
        DetailHeader.HorizontalAlignment = HorizontalAlignment.Stretch;
        DetailHeader.VerticalAlignment = VerticalAlignment.Stretch;
        heroGrid.Children.Add(DetailHeader);
        heroGrid.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 220,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(0, 4, 7, 11), Offset = 0 },
                    new GradientStop { Color = Windows.UI.Color.FromArgb(245, 4, 7, 11), Offset = 1 }
                }
            }
        });

        BackButton.Content = "BACK";
        MakeSquare(BackButton);
        BackButton.HorizontalAlignment = HorizontalAlignment.Left;
        BackButton.VerticalAlignment = VerticalAlignment.Top;
        BackButton.Margin = new Thickness(34, 30, 0, 0);
        BackButton.Width = 126;
        BackButton.Height = 46;
        BackButton.Click += Back_Click;
        heroGrid.Children.Add(BackButton);

        DetailLauncherLogo.Width = 112;
        DetailLauncherLogo.Height = 72;
        DetailLauncherLogo.Stretch = Stretch.Uniform;
        DetailLauncherLogo.Opacity = 0.96;
        DetailLauncherLogoGlow.Width = 154;
        DetailLauncherLogoGlow.Height = 104;
        DetailLauncherLogoGlow.CornerRadius = new CornerRadius(52);
        DetailLauncherLogoGlow.HorizontalAlignment = HorizontalAlignment.Right;
        DetailLauncherLogoGlow.VerticalAlignment = VerticalAlignment.Top;
        DetailLauncherLogoGlow.Margin = new Thickness(0, 54, 96, 0);
        DetailLauncherLogoGlow.Child = DetailLauncherLogo;
        heroGrid.Children.Add(DetailLauncherLogoGlow);

        DetailsPage.Children.Add(heroGrid);

        var contentGrid = new Grid { Margin = new Thickness(54, -54, 54, 18) };
        Grid.SetRow(contentGrid, 1);
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.38, GridUnitType.Star) });

        // Flat cover art: never larger than 600x900, with no perspective or cartridge frame.
        var leftGrid = new Grid();
        leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var coverBorder = new Border
        {
            MaxWidth = 400,
            MaxHeight = 600,
            MinWidth = 180,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 7, 9, 13)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = DetailCover
        };
        DetailCover.Stretch = Stretch.Uniform;
        DetailCover.HorizontalAlignment = HorizontalAlignment.Center;
        DetailCover.VerticalAlignment = VerticalAlignment.Center;
        leftGrid.Children.Add(coverBorder);

        var infoStack = new StackPanel
        {
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(infoStack, 2);
        DetailTitle.FontSize = 42;
        DetailTitle.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        DetailTitle.TextWrapping = TextWrapping.Wrap;
        DetailTitle.Margin = new Thickness(0, 0, 0, 2);
        DetailDescription.FontSize = 17;
        DetailDescription.LineHeight = 27;
        DetailDescription.TextWrapping = TextWrapping.Wrap;
        DetailDescription.MaxHeight = 170;
        DetailMetadata.FontSize = 14;
        DetailMetadata.LineHeight = 24;
        DetailMetadata.TextWrapping = TextWrapping.Wrap;
        DetailMetadata.Opacity = 0.92;
        infoStack.Children.Add(DetailTitle);
        infoStack.Children.Add(DetailDescription);
        infoStack.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 255, 255)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        infoStack.Children.Add(DetailMetadata);
        leftGrid.Children.Add(infoStack);
        contentGrid.Children.Add(leftGrid);

        var trailerGrid = new Grid();
        Grid.SetColumn(trailerGrid, 2);
        trailerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        trailerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        trailerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });

        var trailerBorder = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(2),
            Child = TrailerNativeHost
        };
        trailerGrid.Children.Add(trailerBorder);

        TrailerStatus.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(218, 12, 15, 21));
        TrailerStatus.Visibility = Visibility.Collapsed;
        TrailerStatus.Padding = new Thickness(20);
        TrailerStatusText.Text = "Loading trailer...";
        TrailerStatusText.FontSize = 16;
        TrailerStatusText.TextAlignment = TextAlignment.Center;
        TrailerStatusText.TextWrapping = TextWrapping.Wrap;
        TrailerStatusText.Margin = new Thickness(0, 0, 0, 12);
        TrailerFallback.Content = "OPEN STEAM STORE";
        MakeSquare(TrailerFallback);
        TrailerFallback.Visibility = Visibility.Collapsed;
        TrailerFallback.HorizontalAlignment = HorizontalAlignment.Center;
        TrailerFallback.Click += TrailerFallback_Click;
        var statusStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        statusStack.Children.Add(TrailerStatusText);
        statusStack.Children.Add(TrailerFallback);
        TrailerStatus.Child = statusStack;
        trailerBorder.Child = new Grid();
        ((Grid)trailerBorder.Child).Children.Add(TrailerNativeHost);
        ((Grid)trailerBorder.Child).Children.Add(TrailerStatus);

        Grid.SetRow(LaunchButton, 2);
        LaunchButton.Content = "LAUNCH GAME";
        MakeSquare(LaunchButton);
        LaunchButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        LaunchButton.VerticalAlignment = VerticalAlignment.Stretch;
        LaunchButton.FontSize = 20;
        LaunchButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        LaunchButton.Click += Launch_Click;
        trailerGrid.Children.Add(LaunchButton);
        contentGrid.Children.Add(trailerGrid);
        DetailsPage.Children.Add(contentGrid);

        var detailPromptBorder = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(224, 4, 6, 10)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Padding = new Thickness(28, 0, 28, 0)
        };
        GamepadPromptBar.Orientation = Orientation.Horizontal;
        GamepadPromptBar.HorizontalAlignment = HorizontalAlignment.Center;
        GamepadPromptBar.VerticalAlignment = VerticalAlignment.Center;
        GamepadPromptBar.Spacing = 52;
        GamepadPromptBar.Children.Add(CreateGamepadPrompt("A", "LAUNCH GAME"));
        GamepadPromptBar.Children.Add(CreateGamepadPrompt("X", "PLAY TRAILER"));
        GamepadPromptBar.Children.Add(CreateGamepadPrompt("B", "BACK"));
        detailPromptBorder.Child = GamepadPromptBar;
        Grid.SetRow(detailPromptBorder, 2);
        DetailsPage.Children.Add(detailPromptBorder);

        Root.Children.Add(DetailsPage);
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        appWindow.Title = "Cart Launch Companion";
    }

    private async Task LoadLibraryAsync()
    {
        var games = await _library.LoadAsync(_root);
        foreach (var game in games)
        {
            game.CoverImage = CreateBitmap(First(game.CoverPath, game.HeaderPath));
            game.HeaderImage = CreateBitmap(game.HeaderPath);
            ApplyLauncherCardBranding(game);
            _games.Add(game);
            _ = RefreshArtworkAsync(game);
        }
        var firstGame = games.FirstOrDefault();
        SetLauncherBackground(firstGame?.Launcher ?? "Steam");
        UpdateGameCardLayout(GamesGrid.ActualWidth);
        if (firstGame is not null) GamesGrid.SelectedItem = firstGame;
        GamesGrid.Focus(FocusState.Programmatic);
    }

    private async Task RefreshArtworkAsync(GameDefinition game)
    {
        await _artwork.EnsureArtworkAsync(game);
        game.CoverImage = CreateBitmap(First(game.CoverPath, game.HeaderPath));
        game.HeaderImage = CreateBitmap(game.HeaderPath);
    }

    private void ApplyLauncherCardBranding(GameDefinition game)
    {
        var normalized = LaunchService.NormalizeLauncher(game.Launcher);
        game.LauncherDisplayName = normalized switch
        {
            "DirectExe" => "LOCAL",
            "Epic" => "EPIC GAMES",
            "GOG" => "GOG.COM",
            "Rockstar" => "ROCKSTAR",
            "Ubisoft" => "UBISOFT",
            "Amazon" => "AMAZON GAMES",
            _ => normalized.ToUpperInvariant()
        };

        game.LauncherBannerBrush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = GetLauncherBannerColor(normalized, true), Offset = 0 },
                new GradientStop { Color = GetLauncherBannerColor(normalized, false), Offset = 1 }
            }
        };

        var logoPath = Path.Combine(_root, "Assets", "Launchers", normalized, "Logo.png");
        if (!File.Exists(logoPath))
            logoPath = Path.Combine(_root, "Assets", "Launchers", "DirectExe", "Logo.png");
        game.LauncherLogo = CreateBitmap(logoPath);
    }

    private static Windows.UI.Color GetLauncherBannerColor(string launcher, bool light)
        => launcher switch
        {
            "Xbox" => light ? Windows.UI.Color.FromArgb(255, 34, 139, 34) : Windows.UI.Color.FromArgb(255, 13, 91, 23),
            "Steam" => light ? Windows.UI.Color.FromArgb(255, 36, 94, 166) : Windows.UI.Color.FromArgb(255, 18, 55, 103),
            "Epic" => light ? Windows.UI.Color.FromArgb(255, 82, 85, 92) : Windows.UI.Color.FromArgb(255, 38, 40, 45),
            "GOG" => light ? Windows.UI.Color.FromArgb(255, 167, 103, 0) : Windows.UI.Color.FromArgb(255, 113, 67, 0),
            "Ubisoft" => light ? Windows.UI.Color.FromArgb(255, 0, 137, 180) : Windows.UI.Color.FromArgb(255, 0, 79, 116),
            "Rockstar" => light ? Windows.UI.Color.FromArgb(255, 167, 45, 45) : Windows.UI.Color.FromArgb(255, 105, 20, 23),
            "Amazon" => light ? Windows.UI.Color.FromArgb(255, 218, 126, 0) : Windows.UI.Color.FromArgb(255, 139, 75, 0),
            "Flash" => light ? Windows.UI.Color.FromArgb(255, 226, 96, 22) : Windows.UI.Color.FromArgb(255, 139, 48, 12),
            _ => light ? Windows.UI.Color.FromArgb(255, 112, 66, 168) : Windows.UI.Color.FromArgb(255, 62, 35, 101)
        };

    private void GamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesGrid.SelectedItem is GameDefinition selected)
            SetLauncherBackground(selected.Launcher);
    }

    private void GamesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateGameCardLayout(e.NewSize.Width);

    private void UpdateGameCardLayout(double availableWidth)
    {
        if (GamesGrid.ItemsPanelRoot is not ItemsWrapGrid panel || _games.Count == 0) return;

        var usableWidth = Math.Max(640, availableWidth - GamesGrid.Padding.Left - GamesGrid.Padding.Right);
        var usableHeight = Math.Max(280, GamesGrid.ActualHeight - GamesGrid.Padding.Top - GamesGrid.Padding.Bottom);
        var count = Math.Clamp(_games.Count, 1, 10);
        var horizontalGapAllowance = count * 16.0;
        var widthFromScreen = (usableWidth - horizontalGapAllowance) / count;
        var widthFromHeight = usableHeight / 1.62;
        var width = Math.Clamp(Math.Min(widthFromScreen, widthFromHeight), 112.0, 230.0);

        panel.MaximumRowsOrColumns = 1;
        panel.ItemWidth = Math.Floor(width);
        panel.ItemHeight = Math.Floor(width * 1.58);
    }

    private async void GamesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameDefinition game) await OpenGameAsync(game);
    }

    private async Task OpenGameAsync(GameDefinition game)
    {
        App.StartupLog($"Game selected: {game.Name}; launcher={game.Launcher}; steamId={game.SteamId}; steamMetadataId={game.EffectiveSteamMetadataId}");
        if (_transitioning)
        {
            App.StartupLog("Game selection ignored because a page transition is active.");
            return;
        }
        _currentGame = game;
        SetLauncherBackground(game.Launcher);
        await PopulateDetailsAsync(game);
        await SwitchPageAsync(CollectionPage, DetailsPage);
        await PlayTrailerAsync(game);
        LaunchButton.Focus(FocusState.Programmatic);
    }

    private async Task PopulateDetailsAsync(GameDefinition game)
    {
        SteamMetadata? steam = null;
        var steamMetadataId = game.EffectiveSteamMetadataId;
        if (!string.IsNullOrWhiteSpace(steamMetadataId))
        {
            try
            {
                App.StartupLog($"Requesting Steam metadata for app {steamMetadataId}; launchProvider={game.Launcher}.");
                steam = await _steam.GetMetadataAsync(steamMetadataId);
                game.SteamTrailerUrls = steam?.TrailerUrls ?? [];
                App.StartupLog($"Steam metadata received for app {steamMetadataId}; trailerCount={game.SteamTrailerUrls.Count}.");
            }
            catch (Exception ex)
            {
                App.StartupLog($"Steam metadata failed for {steamMetadataId}: {ex}");
                game.SteamTrailerUrls = [];
            }
        }
        else
        {
            game.SteamTrailerUrls = [];
        }

        game.Description = First(game.Description, steam?.Description);
        game.Developer = First(game.Developer, steam?.Developer);
        game.Publisher = First(game.Publisher, steam?.Publisher);
        game.Genre = First(game.Genre, steam?.Genre);
        game.ReleaseDate = First(game.ReleaseDate, steam?.ReleaseDate);
        game.Website = First(game.Website, steam?.Website);
        game.VideoUrl = First(game.VideoUrl, steam?.TrailerUrl);

        DetailTitle.Text = game.Name;
        DetailDescription.Text = First(game.DetailedDescription, game.Description);
        DetailMetadata.Text = $"DEVELOPER   {Display(game.Developer)}\nPUBLISHER   {Display(game.Publisher)}\nGENRE   {Display(game.Genre)}\nRELEASE DATE   {Display(game.ReleaseDate)}\nLAUNCHER   {Display(game.Launcher)}";
        DetailHeader.Source = CreateBitmap(game.HeaderPath);
        DetailCover.Source = CreateBitmap(First(game.CoverPath, game.HeaderPath));
        UpdateDetailLauncherBrand(game.Launcher);
    }

    private async Task PlayTrailerAsync(GameDefinition game)
    {
        App.StartupLog($"Trailer pipeline entered: {game.Name}; videoUrl={game.VideoUrl}; youtubeUrl={game.YouTubeUrl}; videoFile={game.VideoFile}; steamTrailerCount={game.SteamTrailerUrls.Count}");
        try
        {
            _vlcMediaPlayer?.Stop();
            _vlcMedia?.Dispose();
            _vlcMedia = null;
        }
        catch { }

        TrailerStatusText.Text = "Loading trailer…";
        TrailerStatus.Visibility = Visibility.Visible;
        TrailerFallback.Visibility = Visibility.Collapsed;
        if (_youTubePlayer is not null) _youTubePlayer.Visibility = Visibility.Collapsed;
        ShowVlcVideoHost(false);
        if (_youTubePlayer is not null) _youTubePlayer.Source = null;

        // Resolve YouTube now, but use it only after local and Steam-native
        // sources. Steam adaptive manifests provide the most integrated trailer
        // experience and should not be bypassed merely because a cartridge also
        // contains a YouTube URL.
        Uri? youtubeFallbackUri = null;
        var youtubeSource = NormalizeVideoSource(First(game.YouTubeUrl, game.VideoUrl));
        if (Uri.TryCreate(youtubeSource, UriKind.Absolute, out var youtubeUri) &&
            TryGetYouTubeEmbedUri(youtubeUri, out var youtubeEmbedUri))
        {
            youtubeFallbackUri = youtubeEmbedUri;
            App.StartupLog($"YouTube trailer fallback available: {youtubeEmbedUri}");
        }

        string source = string.Empty;

        // Every cartridge can provide a local metadata-page clip named snaps.mp4.
        // Local clips are preferred because they are immediate and do not depend
        // on a storefront URL remaining directly playable.
        var snapsPath = Path.Combine(game.FolderPath, "snaps.mp4");
        if (File.Exists(snapsPath))
        {
            source = new Uri(Path.GetFullPath(snapsPath)).AbsoluteUri;
            App.StartupLog($"Trailer source selected: local snaps file {snapsPath}");
        }

        // Keep VideoFile support for older cartridges and custom filenames.
        if (string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(game.VideoFile))
        {
            var local = Path.Combine(game.FolderPath, game.VideoFile);
            if (File.Exists(local))
            {
                source = new Uri(Path.GetFullPath(local)).AbsoluteUri;
                App.StartupLog($"Trailer source selected: configured local file {local}");
            }
        }

        if (string.IsNullOrWhiteSpace(source)) source = game.VideoUrl;
        source = NormalizeVideoSource(source);

        // Steam CDN movie URLs are downloaded to a local cache before playback.
        // This avoids intermittent Windows Media Engine failures when streaming
        // directly from Steam's redirecting or range-request CDN endpoints.
        var steamMetadataId = game.EffectiveSteamMetadataId;
        if (!string.IsNullOrWhiteSpace(steamMetadataId))
        {
            var steamCandidates = new[] { source }
                .Concat(game.SteamTrailerUrls)
                .Where(IsSteamRemoteMedia)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            App.StartupLog($"Steam trailer candidates for metadata app {steamMetadataId}: {steamCandidates.Length}");
            var adaptiveSource = steamCandidates.FirstOrDefault(IsAdaptiveManifest);
            if (!string.IsNullOrWhiteSpace(adaptiveSource))
            {
                // DASH/HLS manifests must be streamed. Downloading the manifest
                // alone produces an unusable local file because its segments and
                // audio tracks remain remote.
                source = adaptiveSource;
                App.StartupLog($"Steam adaptive trailer selected: {source}");
            }
            else if (steamCandidates.Length > 0)
            {
                try
                {
                    TrailerStatusText.Text = "Downloading Steam trailer…";
                    var cached = await _steam.CacheFirstAvailableTrailerAsync(steamCandidates, steamMetadataId);
                    if (!string.IsNullOrWhiteSpace(cached))
                    {
                        source = new Uri(Path.GetFullPath(cached)).AbsoluteUri;
                        App.StartupLog($"Steam trailer cached: {cached}; bytes={new FileInfo(cached).Length}");
                    }
                    else
                    {
                        App.StartupLog("Steam trailer cache returned no file.");
                    }
                }
                catch (Exception ex)
                {
                    App.StartupLog($"Steam trailer cache failed for metadata app {steamMetadataId}: {ex}");
                    ShowTrailerFallback($"Steam trailer download failed: {ex.GetBaseException().Message}");
                    return;
                }
            }
        }

        App.StartupLog($"Normalized trailer source: {source}");
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (youtubeFallbackUri is not null)
            {
                App.StartupLog($"Trailer source selected: YouTube fallback {youtubeFallbackUri}");
                await ShowYouTubeTrailerAsync(youtubeFallbackUri);
                return;
            }

            ShowTrailerFallback("No playable trailer was found for this game.");
            return;
        }

        if (TryGetYouTubeEmbedUri(uri, out var embedUri))
        {
            App.StartupLog($"Trailer source selected: YouTube {embedUri}");
            await ShowYouTubeTrailerAsync(embedUri);
            return;
        }

        try
        {
            bool played;
            if (IsAdaptiveManifest(uri.AbsoluteUri))
            {
                // Steam DASH/HLS manifests are VLC-only. Windows Media may accept
                // the source without ever rendering it, which previously produced
                // a false successful result in the log.
                App.StartupLog($"Adaptive trailer routed exclusively to VLC: {uri}");
                played = await PlayWithVlcAsync(uri);
            }
            else if (uri.IsFile)
            {
                // Windows Media is the preferred path for ordinary local MP4/WebM files.
                played = await PlayWithWindowsMediaAsync(uri) || await PlayWithVlcAsync(uri);
            }
            else
            {
                played = await PlayWithVlcAsync(uri) || await PlayWithWindowsMediaAsync(uri);
            }

            App.StartupLog($"Trailer playback engines completed; source={uri}; accepted={played}");
            if (!played && youtubeFallbackUri is not null)
            {
                App.StartupLog($"Native trailer playback failed; selecting YouTube fallback {youtubeFallbackUri}");
                await ShowYouTubeTrailerAsync(youtubeFallbackUri);
                return;
            }

            if (!played)
                ShowTrailerFallback("No trailer playback engine could start this source.");
        }
        catch (Exception ex)
        {
            App.StartupLog("Trailer playback pipeline failed: " + ex);
            ShowTrailerFallback($"Trailer playback failed: {ex.Message}");
        }
    }


    private void EnsureVlcView()
    {
        if (_vlcVideoHwnd != IntPtr.Zero && _vlcMediaPlayer is not null) return;

        try
        {
            App.StartupLog("Initializing LibVLC for bounded Win32 child-window playback.");
            Core.Initialize();
            _libVlc ??= new LibVLC();
            _vlcMediaPlayer ??= new LibVLCSharp.Shared.MediaPlayer(_libVlc) { Volume = 65 };

            var parentHwnd = WindowNative.GetWindowHandle(this);
            _vlcVideoHwnd = CreateWindowExW(
                0,
                "STATIC",
                string.Empty,
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
                0, 0, 1, 1,
                parentHwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_vlcVideoHwnd == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowExW failed with error {Marshal.GetLastWin32Error()}.");

            _vlcMediaPlayer.Hwnd = _vlcVideoHwnd;
            TrailerNativeHost.SizeChanged += (_, _) => UpdateVlcVideoHostBounds();
            TrailerNativeHost.LayoutUpdated += (_, _) => UpdateVlcVideoHostBounds();
            UpdateVlcVideoHostBounds();

            _vlcMediaPlayer.Playing += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                TrailerStatus.Visibility = Visibility.Collapsed;
                TrailerFallback.Visibility = Visibility.Collapsed;
                ShowVlcVideoHost(true);
            });
            _vlcMediaPlayer.EncounteredError += (_, _) => DispatcherQueue.TryEnqueue(() =>
                ShowTrailerFallback("VLC could not play this trailer."));

            App.StartupLog("LibVLC bounded child window created and assigned to MediaPlayer.Hwnd.");
        }
        catch (Exception ex)
        {
            App.StartupLog("VLC bounded video host creation failed: " + ex);
            ShowTrailerFallback($"VLC video host creation failed: {ex.GetBaseException().Message}");
        }
    }

    private void UpdateVlcVideoHostBounds()
    {
        if (_vlcVideoHwnd == IntPtr.Zero || TrailerNativeHost.ActualWidth <= 1 || TrailerNativeHost.ActualHeight <= 1)
            return;

        try
        {
            var point = TrailerNativeHost.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0));
            var parentHwnd = WindowNative.GetWindowHandle(this);
            var scale = GetDpiForWindow(parentHwnd) / 96.0;
            var x = (int)Math.Round(point.X * scale);
            var y = (int)Math.Round(point.Y * scale);
            var width = Math.Max(1, (int)Math.Round(TrailerNativeHost.ActualWidth * scale));
            var height = Math.Max(1, (int)Math.Round(TrailerNativeHost.ActualHeight * scale));
            SetWindowPos(_vlcVideoHwnd, HWND_TOP, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch (Exception ex)
        {
            App.StartupLog("VLC child-window bounds update failed: " + ex.Message);
        }
    }

    private void ShowVlcVideoHost(bool show)
    {
        if (_vlcVideoHwnd == IntPtr.Zero) return;
        ShowWindow(_vlcVideoHwnd, show ? SW_SHOWNA : SW_HIDE);
        if (show) UpdateVlcVideoHostBounds();
    }

    private void DestroyVlcVideoHost()
    {
        if (_vlcVideoHwnd == IntPtr.Zero) return;
        DestroyWindow(_vlcVideoHwnd);
        _vlcVideoHwnd = IntPtr.Zero;
    }

    private async Task<bool> PlayWithVlcAsync(Uri uri)
    {
        try
        {
            if (_youTubePlayer is not null) _youTubePlayer.Visibility = Visibility.Collapsed;
            EnsureVlcView();
            if (_vlcVideoHwnd == IntPtr.Zero || _libVlc is null || _vlcMediaPlayer is null)
            {
                App.StartupLog($"VLC bounded host unavailable; hwnd={_vlcVideoHwnd != IntPtr.Zero}; libVlc={_libVlc is not null}; player={_vlcMediaPlayer is not null}; source={uri}");
                return false;
            }

            ShowVlcVideoHost(true);
            await Task.Delay(50);

            _vlcMediaPlayer.Stop();
            _vlcMedia?.Dispose();
            _vlcMedia = new Media(_libVlc, uri);
            _vlcMedia.AddOption(":network-caching=3000");
            _vlcMedia.AddOption(":live-caching=3000");
            _vlcMedia.AddOption(":file-caching=500");
            _vlcMedia.AddOption(":http-reconnect");
            _vlcMedia.AddOption(":http-user-agent=Mozilla/5.0 CartLaunchCompanion/1.0");
            _vlcMedia.AddOption(":http-referrer=https://store.steampowered.com/");
            _vlcMedia.AddOption(":adaptive-logic=highest");
            _vlcMedia.AddOption(":avcodec-hw=any");
            _vlcMedia.AddOption(":input-repeat=65535");

            TrailerStatusText.Text = IsAdaptiveManifest(uri.AbsoluteUri)
                ? "Opening Steam adaptive stream…"
                : "Opening trailer with VLC…";
            TrailerStatus.Visibility = Visibility.Visible;
            App.StartupLog($"VLC play requested: {uri}; adaptive={IsAdaptiveManifest(uri.AbsoluteUri)}");
            var accepted = _vlcMediaPlayer.Play(_vlcMedia);
            App.StartupLog($"VLC accepted play request: {accepted}");
            if (!accepted) return false;

            // Play() only confirms that VLC accepted the command. Wait until the
            // player actually enters Playing so broken manifests can fall back.
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_vlcMediaPlayer.IsPlaying)
                {
                    App.StartupLog($"VLC confirmed active playback: {uri}");
                    return true;
                }

                if (_vlcMediaPlayer.State is VLCState.Error or VLCState.Ended or VLCState.Stopped)
                {
                    App.StartupLog($"VLC stopped before playback; state={_vlcMediaPlayer.State}; source={uri}");
                    return false;
                }

                await Task.Delay(250);
            }

            App.StartupLog($"VLC playback confirmation timed out; state={_vlcMediaPlayer.State}; source={uri}");
            return false;
        }
        catch (Exception ex)
        {
            App.StartupLog("VLC trailer failed: " + ex);
            ShowVlcVideoHost(false);
            return false;
        }
    }


    private async Task<bool> PlayWithWindowsMediaAsync(Uri uri)
    {
        try
        {
            if (_youTubePlayer is not null) _youTubePlayer.Visibility = Visibility.Collapsed;
            ShowVlcVideoHost(false);

            if (_windowsMediaView is null)
            {
                _windowsMediaPlayer = new Windows.Media.Playback.MediaPlayer
                {
                    IsLoopingEnabled = true,
                    Volume = 0.65
                };
                _windowsMediaPlayer.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    App.StartupLog("Windows Media reported MediaOpened.");
                    TrailerStatus.Visibility = Visibility.Collapsed;
                    TrailerFallback.Visibility = Visibility.Collapsed;
                });
                _windowsMediaPlayer.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
                {
                    App.StartupLog($"Windows Media reported MediaFailed: {args.ErrorMessage}");
                    ShowTrailerFallback($"Windows Media could not play this trailer: {args.ErrorMessage}");
                });

                _windowsMediaView = new MediaPlayerElement
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    AreTransportControlsEnabled = false,
                    AutoPlay = false,
                    Visibility = Visibility.Collapsed
                };
                _windowsMediaView.SetMediaPlayer(_windowsMediaPlayer);
                TrailerNativeHost.Children.Insert(0, _windowsMediaView);
            }

            App.StartupLog($"Windows Media play requested: {uri}");
            _windowsMediaPlayer!.Source = MediaSource.CreateFromUri(uri);
            _windowsMediaView.Visibility = Visibility.Visible;
            TrailerStatusText.Text = "Opening trailer with Windows Media…";
            TrailerStatus.Visibility = Visibility.Visible;
            _windowsMediaPlayer.Play();
            await Task.Delay(250);
            return true;
        }
        catch (Exception ex)
        {
            App.StartupLog("Windows Media trailer failed: " + ex);
            if (_windowsMediaView is not null) _windowsMediaView.Visibility = Visibility.Collapsed;
            return false;
        }
    }

    private WebView2 EnsureYouTubePlayer()
    {
        if (_youTubePlayer is not null) return _youTubePlayer;

        var player = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _youTubePlayer = player;
        TrailerNativeHost.Children.Add(player);
        return player;
    }

    private async Task ShowYouTubeTrailerAsync(Uri embedUri)
    {
        ShowVlcVideoHost(false);
        var youTubePlayer = EnsureYouTubePlayer();
        youTubePlayer.Visibility = Visibility.Visible;
        TrailerStatus.Visibility = Visibility.Collapsed;
        TrailerFallback.Visibility = Visibility.Collapsed;

        try
        {
            TrailerStatusText.Text = "Opening embedded YouTube player…";
            await youTubePlayer.EnsureCoreWebView2Async();

            // YouTube error 153 occurs when /embed is opened as the WebView's
            // top-level page because that request has no enclosing-page Referer.
            // Serve a tiny local page through a virtual HTTPS origin and put the
            // YouTube player in an iframe. WebView2 then supplies the enclosing
            // page origin/referrer in the same way a normal website embed does.
            const string virtualHost = "gamecartridge.local";
            const string virtualOrigin = "https://gamecartridge.local";
            var playerFolder = Path.Combine(
                PortablePaths.DataDirectory, "YouTubePlayer");
            Directory.CreateDirectory(playerFolder);

            var videoId = embedUri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(videoId) ||
                videoId.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            {
                throw new InvalidOperationException("The YouTube video ID is invalid.");
            }

            var iframeSource =
                $"https://www.youtube.com/embed/{Uri.EscapeDataString(videoId)}" +
                "?autoplay=1&mute=1&rel=0&playsinline=1" +
                "&origin=https%3A%2F%2Fgamecartridge.local" +
                "&widget_referrer=https%3A%2F%2Fgamecartridge.local%2F";

            var html = $$"""
                <!doctype html>
                <html>
                <head>
                  <meta charset="utf-8">
                  <meta name="referrer" content="strict-origin-when-cross-origin">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <style>
                    html,body,iframe { width:100%; height:100%; margin:0; border:0; overflow:hidden; background:#000; }
                  </style>
                </head>
                <body>
                  <iframe
                    src="{{iframeSource}}"
                    title="YouTube trailer"
                    referrerpolicy="strict-origin-when-cross-origin"
                    allow="autoplay; encrypted-media; picture-in-picture; fullscreen"
                    allowfullscreen></iframe>
                </body>
                </html>
                """;

            var playerFile = Path.Combine(playerFolder, "player.html");
            await File.WriteAllTextAsync(playerFile, html);

            youTubePlayer.CoreWebView2.SetVirtualHostNameToFolderMapping(
                virtualHost,
                playerFolder,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Deny);

            var navigationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void NavigationCompleted(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
            {
                App.StartupLog($"YouTube WebView navigation completed: success={args.IsSuccess}; status={args.WebErrorStatus}");
                navigationCompletion.TrySetResult(args.IsSuccess);
            }

            youTubePlayer.CoreWebView2.NavigationCompleted += NavigationCompleted;
            try
            {
                App.StartupLog($"YouTube WebView navigating to {virtualOrigin}/player.html for video {videoId}.");
                youTubePlayer.CoreWebView2.Navigate($"{virtualOrigin}/player.html");
                var completed = await Task.WhenAny(navigationCompletion.Task, Task.Delay(TimeSpan.FromSeconds(12)));
                var success = completed == navigationCompletion.Task && await navigationCompletion.Task;
                if (!success)
                {
                    App.StartupLog("Embedded YouTube playback did not initialize; opening the original YouTube URL externally.");
                    var external = new Uri($"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}");
                    var launched = await Windows.System.Launcher.LaunchUriAsync(external);
                    if (!launched) throw new InvalidOperationException("Windows could not open the YouTube trailer.");
                    ShowTrailerFallback("The trailer was opened in your browser.");
                }
                else
                {
                    App.StartupLog("Embedded YouTube player page loaded successfully.");
                }
            }
            finally
            {
                youTubePlayer.CoreWebView2.NavigationCompleted -= NavigationCompleted;
            }
        }
        catch (Exception ex)
        {
            if (_youTubePlayer is not null) _youTubePlayer.Visibility = Visibility.Collapsed;
            ShowTrailerFallback($"YouTube player failed: {ex.Message}");
        }
    }

    private static bool IsAdaptiveManifest(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));

    private static bool IsSteamRemoteMedia(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return false;
        if (uri.IsFile) return false;
        var host = uri.Host;
        return host.Contains("steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("akamaihd.net", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("steampowered.com", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("cloudflare.steamstatic.com", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowTrailerFallback(string message = "Trailer unavailable.")
    {
        App.StartupLog("Trailer fallback shown: " + message);
        TrailerStatusText.Text = message;
        TrailerStatus.Visibility = Visibility.Visible;
        TrailerFallback.Visibility = _currentGame is not null &&
                                     !string.IsNullOrWhiteSpace(_currentGame.EffectiveSteamMetadataId)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task SwitchPageAsync(UIElement from, UIElement to)
    {
        _transitioning = true;
        to.Visibility = Visibility.Visible;
        to.Opacity = 0;

        var fadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EnableDependentAnimation = true };
        var fadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(180), EnableDependentAnimation = true };
        await AnimateAsync(from, fadeOut);
        from.Visibility = Visibility.Collapsed;
        await AnimateAsync(to, fadeIn);
        _transitioning = false;
    }

    private static Task AnimateAsync(UIElement element, Timeline animation)
    {
        var completion = new TaskCompletionSource();
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => completion.TrySetResult();
        storyboard.Begin();
        return completion.Task;
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        try { await NavigateBackAsync(); }
        catch (Exception ex) { Debug.WriteLine($"Back navigation failed: {ex}"); }
    }

    private async Task NavigateBackAsync()
    {
        if (_transitioning || DetailsPage.Visibility != Visibility.Visible) return;
        StopTrailerPlayback();
        await SwitchPageAsync(DetailsPage, CollectionPage);
        GamesGrid.Focus(FocusState.Programmatic);
    }

    private void StopTrailerPlayback()
    {
        try
        {
            _vlcMediaPlayer?.Stop();
            _vlcMedia?.Dispose();
            _vlcMedia = null;
        }
        catch { }

        try
        {
            if (_youTubePlayer?.CoreWebView2 is not null)
                _youTubePlayer.CoreWebView2.Navigate("about:blank");
            else if (_youTubePlayer is not null)
                _youTubePlayer.Source = null;
        }
        catch { }

        if (_youTubePlayer is not null) _youTubePlayer.Visibility = Visibility.Collapsed;
        ShowVlcVideoHost(false);
        try { _windowsMediaPlayer?.Pause(); } catch { }
        if (_windowsMediaView is not null) _windowsMediaView.Visibility = Visibility.Collapsed;
    }

    private void StopTrailerAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        _gamepadTimer.Stop();
        StopTrailerPlayback();
        Close();
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGame is null) return;
        try
        {
            App.StartupLog($"Launch requested: {_currentGame.Name}; launcher={_currentGame.Launcher}; steamId={_currentGame.SteamId}; executable={_currentGame.Executable}; launchUri={_currentGame.LaunchUri}");
            var launchedGame = _currentGame;
            StopTrailerPlayback();
            App.StartupLog($"Trailer playback stopped before launching {launchedGame.Name}.");
            LaunchService.Launch(launchedGame);
            App.StartupLog($"Launch command submitted successfully: {launchedGame.Name}");
            HideLauncherAfterLaunch();
            _ = MonitorGameAndRestoreAsync(launchedGame);
        }
        catch (Exception ex)
        {
            App.StartupLog("Launch failed: " + ex);
            await ShowErrorAsync(ex.GetBaseException().Message);
        }
    }



    private async Task MonitorGameAndRestoreAsync(GameDefinition game)
    {
        if (!game.RestoreOnExit)
        {
            App.StartupLog($"Restore-on-exit disabled for {game.Name}.");
            return;
        }

        var processName = (game.ProcessName ?? string.Empty).Trim();
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            processName = processName[..^4];
        if (string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(game.Executable))
            processName = Path.GetFileNameWithoutExtension(game.Executable);
        if (string.IsNullOrWhiteSpace(processName))
        {
            App.StartupLog($"No ProcessName available for {game.Name}; restoring launcher immediately instead of leaving it hidden.");
            DispatcherQueue.TryEnqueue(RestoreLauncherAfterGame);
            return;
        }

        var startTimeout = TimeSpan.FromSeconds(Math.Clamp(game.ProcessStartTimeoutSeconds, 5, 600));
        var pollDelay = TimeSpan.FromSeconds(Math.Clamp(game.ProcessExitPollSeconds, 1, 30));
        var deadline = DateTime.UtcNow + startTimeout;
        App.StartupLog($"Waiting for game process '{processName}' for up to {startTimeout.TotalSeconds:0} seconds.");

        Process[] matches = [];
        while (DateTime.UtcNow < deadline)
        {
            matches = Process.GetProcessesByName(processName);
            if (matches.Length > 0) break;
            await Task.Delay(pollDelay);
        }

        if (matches.Length == 0)
        {
            App.StartupLog($"Game process '{processName}' was not detected before timeout; restoring launcher as a safety fallback.");
            DispatcherQueue.TryEnqueue(RestoreLauncherAfterGame);
            return;
        }

        App.StartupLog($"Detected {matches.Length} process(es) named '{processName}'. Waiting for exit.");
        while (Process.GetProcessesByName(processName).Length > 0)
            await Task.Delay(pollDelay);

        App.StartupLog($"Game process '{processName}' exited; restoring launcher.");
        DispatcherQueue.TryEnqueue(RestoreLauncherAfterGame);
    }

    private async void RestoreLauncherAfterGame()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Re-enter a normal visible state first. Transitioning directly from a
            // minimized/hidden fullscreen presenter is unreliable on some systems.
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            ShowWindow(hwnd, SW_SHOW);
            ShowWindow(hwnd, SW_RESTORE);
            Activate();
            await Task.Delay(180);

            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            ShowWindow(hwnd, SW_SHOW);
            Activate();
            ForceLauncherToForeground(hwnd);
            await Task.Delay(180);
            ForceLauncherToForeground(hwnd);
            LaunchButton.Focus(FocusState.Programmatic);
            App.StartupLog("Launcher shown, restored to fullscreen, and forced to the foreground after game exit.");
        }
        catch (Exception ex)
        {
            App.StartupLog("Launcher restore failed: " + ex);
        }
    }


    private static void ForceLauncherToForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0u
            : GetWindowThreadProcessId(foreground, IntPtr.Zero);

        var attached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attached = AttachThreadInput(currentThread, foregroundThread, true);

            ShowWindow(hwnd, SW_RESTORE);
            BringWindowToTop(hwnd);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            SetActiveWindow(hwnd);
            SetFocus(hwnd);
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private void HideLauncherAfterLaunch()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_HIDE);
            App.StartupLog("Launcher hidden after successful game launch.");
        }
        catch (Exception ex)
        {
            App.StartupLog("Launcher hide failed: " + ex);
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        try
        {
            if (Root.XamlRoot is null)
            {
                TrailerStatusText.Text = "Unable to launch: " + message;
                TrailerStatus.Visibility = Visibility.Visible;
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Unable to launch",
                Content = message,
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
        catch (Exception dialogEx)
        {
            App.StartupLog("Launch error dialog failed: " + dialogEx);
            TrailerStatusText.Text = "Unable to launch: " + message;
            TrailerStatus.Visibility = Visibility.Visible;
        }
    }

    private void TrailerFallback_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGame is null || string.IsNullOrWhiteSpace(_currentGame.EffectiveSteamMetadataId)) return;
        Process.Start(new ProcessStartInfo($"https://store.steampowered.com/app/{_currentGame.EffectiveSteamMetadataId}/") { UseShellExecute = true });
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => RequestExitOrConfirm();

    private void RequestExitOrConfirm()
    {
        if (_exitConfirmationVisible)
        {
            StopTrailerAndClose();
            return;
        }

        _exitConfirmationVisible = true;
        ExitConfirmationOverlay.Visibility = Visibility.Visible;
        ExitConfirmationOverlay.Opacity = 1;
    }

    private void CancelExitConfirmation()
    {
        if (!_exitConfirmationVisible) return;
        _exitConfirmationVisible = false;
        ExitConfirmationOverlay.Visibility = Visibility.Collapsed;
        GamesGrid.Focus(FocusState.Programmatic);
    }

    private void Root_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (DetailsPage.Visibility == Visibility.Visible) Back_Click(sender, e);
        else RequestExitOrConfirm();
    }

    private void GamepadTimer_Tick(object? sender, object e)
    {
        if (_isClosing || _transitioning || _gamepadActionInProgress || Root.XamlRoot is null) return;

        try
        {
            // The Gamepads collection can change between enumeration and reading.
            // Snapshot it, then tolerate a controller disappearing mid-poll.
            var gamepads = Gamepad.Gamepads.ToArray();
            var gamepad = gamepads.FirstOrDefault();
            if (gamepad is null)
            {
                _previousGamepadButtons = GamepadButtons.None;
                return;
            }

            GamepadReading reading;
            try { reading = gamepad.GetCurrentReading(); }
            catch
            {
                _previousGamepadButtons = GamepadButtons.None;
                return;
            }

            var buttons = reading.Buttons;
            var pressed = buttons & ~_previousGamepadButtons;
            _previousGamepadButtons = buttons;

            var canRepeat = DateTime.UtcNow - _lastGamepadMove > TimeSpan.FromMilliseconds(180);
            FocusNavigationDirection? direction = null;
            if (pressed.HasFlag(GamepadButtons.DPadLeft) || (canRepeat && reading.LeftThumbstickX < -0.55)) direction = FocusNavigationDirection.Left;
            else if (pressed.HasFlag(GamepadButtons.DPadRight) || (canRepeat && reading.LeftThumbstickX > 0.55)) direction = FocusNavigationDirection.Right;
            else if (pressed.HasFlag(GamepadButtons.DPadUp) || (canRepeat && reading.LeftThumbstickY > 0.55)) direction = FocusNavigationDirection.Up;
            else if (pressed.HasFlag(GamepadButtons.DPadDown) || (canRepeat && reading.LeftThumbstickY < -0.55)) direction = FocusNavigationDirection.Down;

            if (direction is not null)
            {
                MoveGamepadFocus(direction.Value);
                _lastGamepadMove = DateTime.UtcNow;
            }

            if (pressed.HasFlag(GamepadButtons.A))
            {
                if (_exitConfirmationVisible) CancelExitConfirmation();
                else _ = RunGamepadActionAsync(ActivateFocusedElementAsync);
            }
            else if (pressed.HasFlag(GamepadButtons.B))
                _ = RunGamepadActionAsync(async () =>
                {
                    if (DetailsPage.Visibility == Visibility.Visible) await NavigateBackAsync();
                    else RequestExitOrConfirm();
                });
        }
        catch (Exception ex)
        {
            // A polling failure must never terminate the UI thread.
            Debug.WriteLine($"Gamepad polling failed: {ex}");
            _previousGamepadButtons = GamepadButtons.None;
        }
    }

    private void MoveGamepadFocus(FocusNavigationDirection direction)
    {
        // Do not use generic FocusManager traversal here. The LibVLC VideoView
        // owns a native swap-chain surface and must never become a gamepad focus target.
        if (CollectionPage.Visibility == Visibility.Visible)
        {
            if (_games.Count == 0) return;
            var index = GamesGrid.SelectedIndex;
            if (index < 0) index = 0;

            var columns = GamesGrid.ItemsPanelRoot is ItemsWrapGrid panel && panel.ItemWidth > 0
                ? Math.Max(1, (int)Math.Floor(Math.Max(1, GamesGrid.ActualWidth) / panel.ItemWidth))
                : 1;

            index = direction switch
            {
                FocusNavigationDirection.Left => Math.Max(0, index - 1),
                FocusNavigationDirection.Right => Math.Min(_games.Count - 1, index + 1),
                FocusNavigationDirection.Up => Math.Max(0, index - columns),
                FocusNavigationDirection.Down => Math.Min(_games.Count - 1, index + columns),
                _ => index
            };

            GamesGrid.SelectedIndex = index;
            GamesGrid.ScrollIntoView(_games[index]);
            if (GamesGrid.ContainerFromIndex(index) is GridViewItem container)
                container.Focus(FocusState.Programmatic);
            else
                GamesGrid.Focus(FocusState.Programmatic);
            return;
        }

        if (DetailsPage.Visibility == Visibility.Visible)
        {
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
            Button target = focused switch
            {
                var value when ReferenceEquals(value, BackButton) =>
                    direction is FocusNavigationDirection.Right or FocusNavigationDirection.Down ? LaunchButton : BackButton,
                var value when ReferenceEquals(value, ExitDetailsButton) =>
                    direction is FocusNavigationDirection.Left or FocusNavigationDirection.Down ? LaunchButton : ExitDetailsButton,
                _ => direction == FocusNavigationDirection.Left ? BackButton :
                     direction == FocusNavigationDirection.Right ? ExitDetailsButton : LaunchButton
            };
            target.Focus(FocusState.Programmatic);
        }
    }

    private async Task RunGamepadActionAsync(Func<Task> action)
    {
        if (_gamepadActionInProgress || _isClosing) return;
        _gamepadActionInProgress = true;
        try { await action(); }
        catch (Exception ex) { Debug.WriteLine($"Gamepad action failed: {ex}"); }
        finally { _gamepadActionInProgress = false; }
    }

    private async Task ActivateFocusedElementAsync()
    {
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        if (focused is Button button)
        {
            if (ReferenceEquals(button, LaunchButton)) Launch_Click(button, new RoutedEventArgs());
            else if (ReferenceEquals(button, BackButton)) await NavigateBackAsync();
            else if (ReferenceEquals(button, ExitCollectionButton) || ReferenceEquals(button, ExitDetailsButton)) StopTrailerAndClose();
            else if (ReferenceEquals(button, TrailerFallback)) TrailerFallback_Click(button, new RoutedEventArgs());
            return;
        }

        if (CollectionPage.Visibility == Visibility.Visible)
        {
            if (focused is GridViewItem item && item.DataContext is GameDefinition focusedGame)
                await OpenGameAsync(focusedGame);
            else if (GamesGrid.SelectedItem is GameDefinition selected)
                await OpenGameAsync(selected);
        }
    }


    private static string NormalizeVideoSource(string? source)
    {
        var value = source?.Trim().Trim('"', '\'') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // Game.json entries are often pasted as youtube.com/... without a scheme.
        if (value.StartsWith("www.youtube.com/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("youtube.com/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("youtu.be/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("www.youtu.be/", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        return value;
    }

    private static bool TryGetYouTubeEmbedUri(Uri uri, out Uri embedUri)
    {
        embedUri = null!;
        var host = uri.Host.ToLowerInvariant().TrimEnd('.');
        string id = string.Empty;

        if (host is "youtu.be" or "www.youtu.be")
            id = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault() ?? string.Empty;
        else if (host.EndsWith("youtube.com", StringComparison.Ordinal))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] is "embed" or "shorts") id = parts[1];
            else
            {
                id = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(part => part.Length == 2 && string.Equals(part[0], "v", StringComparison.OrdinalIgnoreCase))
                    .Select(part => Uri.UnescapeDataString(part[1]))
                    .FirstOrDefault() ?? string.Empty;
            }
        }

        id = id.Split('?', '#', '&').FirstOrDefault()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id)) return false;
        embedUri = new Uri($"https://www.youtube.com/embed/{Uri.EscapeDataString(id)}?autoplay=1&rel=0&playsinline=1&origin=https%3A%2F%2Fgamecartridge.local");
        return true;
    }

    private void UpdateDetailLauncherBrand(string launcher)
    {
        var normalized = LaunchService.NormalizeLauncher(launcher);
        var logoPath = Path.Combine(_root, "Assets", "Launchers", normalized, "Logo.png");
        if (!File.Exists(logoPath))
            logoPath = Path.Combine(_root, "Assets", "Launchers", "DirectExe", "Logo.png");
        DetailLauncherLogo.Source = File.Exists(logoPath) ? CreateBitmap(logoPath) : null;

        var accent = normalized switch
        {
            "Xbox" => Windows.UI.Color.FromArgb(80, 82, 196, 26),
            "Steam" => Windows.UI.Color.FromArgb(80, 62, 139, 255),
            "Epic" => Windows.UI.Color.FromArgb(68, 210, 210, 216),
            "GOG" => Windows.UI.Color.FromArgb(80, 174, 76, 224),
            "Ubisoft" => Windows.UI.Color.FromArgb(80, 36, 184, 224),
            "Rockstar" => Windows.UI.Color.FromArgb(80, 255, 190, 30),
            "Amazon" => Windows.UI.Color.FromArgb(80, 255, 153, 0),
            "Flash" => Windows.UI.Color.FromArgb(80, 255, 116, 28),
            _ => Windows.UI.Color.FromArgb(72, 157, 86, 232)
        };
        DetailLauncherLogoGlow.Background = new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop { Color = accent, Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = 1 }
            }
        };
    }

    private void SetLauncherBackground(string launcher)
    {
        var normalized = LaunchService.NormalizeLauncher(launcher);
        var path = Path.Combine(_root, "Assets", "Launchers", normalized, "Background.png");
        if (!File.Exists(path))
            path = Path.Combine(_root, "Assets", "Launchers", "DirectExe", "Background.png");
        BackgroundImage.Source = CreateBitmap(path);
        BackgroundImage.Opacity = CollectionPage.Visibility == Visibility.Visible ? 0.16 : 0.50;

        var accent = normalized switch
        {
            "Xbox" => Windows.UI.Color.FromArgb(62, 82, 196, 26),
            "Steam" => Windows.UI.Color.FromArgb(62, 62, 139, 255),
            "Epic" => Windows.UI.Color.FromArgb(48, 210, 210, 216),
            "GOG" => Windows.UI.Color.FromArgb(62, 174, 76, 224),
            "Ubisoft" => Windows.UI.Color.FromArgb(62, 36, 184, 224),
            "Rockstar" => Windows.UI.Color.FromArgb(62, 255, 190, 30),
            "Amazon" => Windows.UI.Color.FromArgb(62, 255, 153, 0),
            "Flash" => Windows.UI.Color.FromArgb(62, 255, 116, 28),
            _ => Windows.UI.Color.FromArgb(54, 157, 86, 232)
        };
        LauncherLogoGlow.Background = new SolidColorBrush(accent);

        var logoPath = Path.Combine(_root, "Assets", "Launchers", normalized, "Logo.png");
        if (!File.Exists(logoPath))
            logoPath = Path.Combine(_root, "Assets", "Launchers", "DirectExe", "Logo.png");
        LauncherBrandLogo.Source = File.Exists(logoPath) ? CreateBitmap(logoPath) : null;
    }

    private static BitmapImage? CreateBitmap(string path)
        => File.Exists(path) ? new BitmapImage(new Uri(ToFileUri(path))) : null;

    private static string ToFileUri(string path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static string First(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_SHOWNA = 8;
    private const int SW_RESTORE = 9;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
