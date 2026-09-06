using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Threading;
using CartLaunchCompanion.Core.Metadata;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Desktop.Input;
using CartLaunchCompanion.Desktop.ViewModels;

namespace CartLaunchCompanion.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly HomeView _homeView = new();
    private readonly MetadataView _metadataView = new();
    private readonly NativeWebView? _exophaseSyncWebView;
    private MainViewModel? _pageViewModel;
    private bool _exophaseSyncPending;
    private bool _exophaseSyncRunning;
    private string _exophasePlayerId = "";

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            _exophaseSyncWebView = new NativeWebView
            {
                Width = 2,
                Height = 2,
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };
            _exophaseSyncWebView.NavigationCompleted += ExophaseNavigationCompleted;
            _exophaseSyncWebView.WebMessageReceived += ExophaseWebMessageReceived;
            RootGrid.Children.Insert(0, _exophaseSyncWebView);
            Opened += (_, _) => StartPendingExophaseSync();
        }

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
        {
            _pageViewModel.PropertyChanged -= OnPageViewModelPropertyChanged;
            _pageViewModel.ExophaseBrowserSyncRequested -= OnExophaseBrowserSyncRequested;
        }

        _pageViewModel = DataContext as MainViewModel;
        _homeView.DataContext = _pageViewModel;
        _metadataView.DataContext = _pageViewModel;

        if (_pageViewModel is null)
            return;

        _pageViewModel.PropertyChanged += OnPageViewModelPropertyChanged;
        _pageViewModel.ExophaseBrowserSyncRequested += OnExophaseBrowserSyncRequested;
        ShowPage(_pageViewModel.ActivePageIndex);
    }

    private void OnExophaseBrowserSyncRequested(object? sender, EventArgs e)
    {
        _exophaseSyncPending = true;
        Dispatcher.UIThread.Post(StartPendingExophaseSync);
    }

    private async void StartPendingExophaseSync()
    {
        if (!_exophaseSyncPending || _exophaseSyncRunning || !IsVisible ||
            _exophaseSyncWebView is null)
            return;

        _exophaseSyncPending = false;
        _exophasePlayerId = (await MetadataSecretStore.ReadAsync(
            MetadataSecretStore.ExophasePlayerId)).Trim();
        if (_exophasePlayerId.Length == 0 || !_exophasePlayerId.All(char.IsDigit))
            return;

        _exophaseSyncRunning = true;
        _exophaseSyncWebView.Source = new Uri(
            $"https://www.exophase.com/?clc_sync={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    private async void ExophaseNavigationCompleted(
        object? sender,
        WebViewNavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || !_exophaseSyncRunning || _exophaseSyncWebView is null ||
            _pageViewModel is null)
            return;

        var titlesJson = JsonSerializer.Serialize(_pageViewModel.Games
            .Select(game => game.Entry.Configuration.Game.Name)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        try
        {
            await _exophaseSyncWebView.InvokeScript($$"""
                (async () => {
                    try {
                        const allGames = [];
                        const wantedTitles = new Set({{titlesJson}}.map(value =>
                            String(value).toLowerCase().replace(/[^a-z0-9]/g, '')));
                        for (let page = 1; page <= 200; page++) {
                            const response = await fetch(
                                `https://api.exophase.com/public/player/{{_exophasePlayerId}}/games?page=${page}&environment=&sort=1&showHidden=0`,
                                { credentials: 'include' });
                            if (!response.ok) {
                                invokeCSharpAction(`CLC_EXO_ERROR:${response.status}`);
                                return;
                            }
                            const data = await response.json();
                            const games = Array.isArray(data.games) ? data.games : [];
                            if (!data.success || games.length === 0) break;
                            allGames.push(...games);
                        }

                        for (const game of allGames.filter(game => {
                            const title = String(game?.meta?.title ?? '').toLowerCase().replace(/[^a-z0-9]/g, '');
                            return wantedTitles.has(title) && Number(game?.earned_awards ?? 0) > 0;
                        })) {
                            const playerGameId = Number(game?.master_playerid);
                            const catalogGameId = Number(game?.master_id);
                            const awardsUrl = String(game?.meta?.endpoint_awards ?? '').split('#')[0];
                            if (!Number.isSafeInteger(playerGameId) || playerGameId <= 0 ||
                                !Number.isSafeInteger(catalogGameId) || catalogGameId <= 0 || !awardsUrl)
                                continue;
                            try {
                                const earnedResponse = await fetch(
                                    `https://api.exophase.com/public/player/${playerGameId}/game/${catalogGameId}/earned`,
                                    { credentials: 'include' });
                                if (!earnedResponse.ok) continue;
                                const earned = await earnedResponse.json();
                                if (!earned?.success || !Array.isArray(earned.list) || earned.list.length === 0)
                                    continue;
                                const awardsResponse = await fetch(awardsUrl, { credentials: 'include' });
                                if (!awardsResponse.ok) continue;
                                const awardsDocument = new DOMParser().parseFromString(
                                    await awardsResponse.text(), 'text/html');
                                const definitions = new Map(
                                    Array.from(awardsDocument.querySelectorAll('li.award')).map(item => [
                                        String(item.id),
                                        {
                                            title: item.querySelector('.award-title')?.textContent?.trim() ?? '',
                                            description: item.querySelector('.award-description')?.textContent?.trim() ?? '',
                                            image: item.querySelector('img.award-image')?.src ?? ''
                                        }
                                    ]));
                                const awards = earned.list.map(item => ({
                                    meta: definitions.get(String(item.awardid)) ?? {},
                                    earned_utc: Number(item.timestamp) || 0
                                })).filter(item => item.meta.title);
                                if (awards.length > 0) {
                                    game._clcGameId = playerGameId;
                                    game._earned = { awards };
                                }
                            } catch { }
                        }
                        invokeCSharpAction('CLC_EXO_DATA:' + JSON.stringify(allGames));
                    } catch {
                        invokeCSharpAction('CLC_EXO_ERROR:browser');
                    }
                })();
                """);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"Exophase browser sync could not start ({ex.GetType().Name}).");
            _exophaseSyncRunning = false;
        }
    }

    private async void ExophaseWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        var message = DecodeWebMessage(e.Body);
        if (message.StartsWith("CLC_EXO_ERROR:", StringComparison.Ordinal))
        {
            System.Diagnostics.Trace.WriteLine($"Exophase browser sync failed ({message[14..]}). ");
            _exophaseSyncRunning = false;
            return;
        }

        if (!message.StartsWith("CLC_EXO_DATA:", StringComparison.Ordinal) ||
            message.Length > 16 * 1024 * 1024)
            return;

        try
        {
            var count = await ExophaseClient.ImportBrowserGamesAsync(
                _exophasePlayerId,
                message[13..],
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CartLaunchCompanion", "Cache", "Exophase"));
            System.Diagnostics.Trace.WriteLine($"Exophase launch sync updated {count} games.");
            _pageViewModel?.NotifyExophaseBrowserSyncCompleted();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"Exophase browser sync could not save data ({ex.GetType().Name}).");
        }
        finally
        {
            _exophaseSyncRunning = false;
        }
    }

    private static string DecodeWebMessage(string? body)
    {
        var value = body?.Trim() ?? "";
        for (var layer = 0; layer < 4 && value.Length >= 2 && value[0] == '"' && value[^1] == '"'; layer++)
        {
            try { value = JsonSerializer.Deserialize<string>(value)?.Trim() ?? ""; }
            catch (JsonException) { break; }
        }
        return value;
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
