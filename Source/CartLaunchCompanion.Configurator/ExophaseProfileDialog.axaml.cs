using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CartLaunchCompanion.Core.Metadata;

namespace CartLaunchCompanion.Configurator;

public sealed partial class ExophaseProfileDialog : Window
{
    private readonly Uri _profileUri;
    private readonly string _knownPlayerId;
    private readonly string _cartTitlesJson;
    private readonly DispatcherTimer _probeTimer;
    private bool _probeRunning;
    private bool _syncStarted;

    public ExophaseProfileDialog() : this("https://www.exophase.com/") { }

    public ExophaseProfileDialog(
        string profileUrl,
        string knownPlayerId = "",
        IReadOnlyCollection<string>? cartTitles = null)
    {
        InitializeComponent();
        _profileUri = new Uri(profileUrl);
        _knownPlayerId = knownPlayerId.Trim();
        _cartTitlesJson = JsonSerializer.Serialize(
            (cartTitles ?? []).Where(title => !string.IsNullOrWhiteSpace(title)).Distinct().ToArray());
        if (!_profileUri.Host.EndsWith("exophase.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only public Exophase profile pages can be opened here.", nameof(profileUrl));

        _probeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _probeTimer.Tick += ProbeTimerTick;
        Opened += DialogOpened;
        Closed += (_, _) => _probeTimer.Stop();
    }

    private void DialogOpened(object? sender, EventArgs e)
    {
        FitToOwner();
        ProfileWebView.Source = _profileUri;
        _probeTimer.Start();
    }

    private void FitToOwner()
    {
        const double edgeRoom = 48;
        var availableWidth = Owner?.ClientSize.Width ?? 980;
        var availableHeight = Owner?.ClientSize.Height ?? 700;
        Width = Math.Max(MinWidth, Math.Min(980, availableWidth - edgeRoom));
        Height = Math.Max(MinHeight, Math.Min(700, availableHeight - edgeRoom));
    }

    private async void ProfileNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        ProfileStatusText.Text = e.IsSuccess
            ? "Waiting for the public profile…"
            : "The page did not finish loading. Check the connection and try again.";
        if (!e.IsSuccess) return;

        if (_knownPlayerId.Length > 0)
            await StartGameSyncAsync(_knownPlayerId);
        else
            await ProbeForPlayerIdAsync();
    }

    private async void ProbeTimerTick(object? sender, EventArgs e) => await ProbeForPlayerIdAsync();

    private async Task ProbeForPlayerIdAsync()
    {
        if (_probeRunning || _syncStarted) return;
        _probeRunning = true;
        try
        {
            await ProfileWebView.InvokeScript("""
                (() => {
                    const value = window.playerProfileId;
                    if (value !== undefined && value !== null && String(value).length > 0)
                        invokeCSharpAction('EXO_ID:' + String(value));
                })();
                """);
        }
        catch
        {
            // Cloudflare transitions can briefly make the document unavailable.
        }
        finally { _probeRunning = false; }
    }

    private void ProfileWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        var message = DecodeWebMessage(e.Body);
        if (message.StartsWith("EXO_ID:", StringComparison.Ordinal))
        {
            var playerId = message[7..];
            if (playerId.Length > 0 && playerId.All(char.IsDigit))
                _ = StartGameSyncAsync(playerId);
            return;
        }

        if (message.StartsWith("EXO_ERROR:", StringComparison.Ordinal))
        {
            _syncStarted = false;
            ProfileStatusText.Text = "Exophase did not return the public game list. Leave this window open, then retry after the page finishes loading.";
            return;
        }

        if (message.StartsWith("EXO_DATA:", StringComparison.Ordinal))
            _ = SaveGameCacheAndCloseAsync(message[9..]);
    }

    private async Task StartGameSyncAsync(string playerId)
    {
        if (_syncStarted || !playerId.All(char.IsDigit)) return;

        _syncStarted = true;
        _probeTimer.Stop();
        ProfileStatusText.Text = "Profile connected. Syncing public game activity…";
        try
        {
            await ProfileWebView.InvokeScript($$"""
                (async () => {
                    try {
                        const allGames = [];
                        const wantedTitles = new Set({{_cartTitlesJson}}.map(value =>
                            String(value).toLowerCase().replace(/[^a-z0-9]/g, '')));
                        for (let page = 1; page <= 200; page++) {
                            const url = `https://api.exophase.com/public/player/{{playerId}}/games?page=${page}&environment=&sort=1&showHidden=0`;
                            const response = await fetch(url, { credentials: 'include' });
                            if (!response.ok) {
                                invokeCSharpAction(`EXO_ERROR:${response.status}`);
                                return;
                            }
                            const data = await response.json();
                            const games = Array.isArray(data.games) ? data.games : [];
                            if (!data.success || games.length === 0) break;
                            allGames.push(...games);
                        }

                        const detailGames = wantedTitles.size === 0
                            ? []
                            : allGames.filter(game => {
                                const title = String(game?.meta?.title ?? '').toLowerCase().replace(/[^a-z0-9]/g, '');
                                return wantedTitles.has(title) && Number(game?.earned_awards ?? 0) > 0;
                            });
                        for (const game of detailGames) {
                            const playerGameId = Number(game?.master_playerid);
                            const catalogGameId = Number(game?.master_id);
                            const awardsUrl = String(game?.meta?.endpoint_awards ?? '').split('#')[0];
                            if (!Number.isSafeInteger(playerGameId) || playerGameId <= 0 ||
                                !Number.isSafeInteger(catalogGameId) || catalogGameId <= 0 ||
                                !awardsUrl) continue;

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
                                if (awards.length === 0) continue;

                                game._clcGameId = playerGameId;
                                game._earned = { awards };
                            } catch {
                                // Individual rows are optional; aggregate progress still remains available.
                            }
                        }
                        invokeCSharpAction('EXO_DATA:' + JSON.stringify({ playerId: '{{playerId}}', games: allGames }));
                    } catch (error) {
                        invokeCSharpAction('EXO_ERROR:browser');
                    }
                })();
                """);
        }
        catch
        {
            _syncStarted = false;
            ProfileStatusText.Text = "The browser could not start the public game sync. Wait for the profile to finish loading, then retry.";
        }
    }

    private async Task SaveGameCacheAndCloseAsync(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("playerId", out var playerIdNode) ||
                !root.TryGetProperty("games", out var gamesNode) ||
                gamesNode.ValueKind != JsonValueKind.Array)
                throw new JsonException("The browser returned an unexpected Exophase response.");

            var playerId = playerIdNode.ValueKind switch
            {
                JsonValueKind.String => playerIdNode.GetString() ?? "",
                JsonValueKind.Number => playerIdNode.GetRawText(),
                _ => ""
            };
            var gamesJson = gamesNode.GetRawText();
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CartLaunchCompanion", "Cache", "Exophase");
            var count = await ExophaseClient.ImportBrowserGamesAsync(playerId, gamesJson, cacheDirectory);
            if (count == 0)
            {
                _syncStarted = false;
                ProfileStatusText.Text = "The public profile did not return any games. Confirm that game activity is public, then retry.";
                return;
            }

            ProfileStatusText.Text = $"Synced {count} public games.";
            Close(playerId);
        }
        catch (Exception ex)
        {
            _syncStarted = false;
            WriteSyncDiagnostic(ex);
            ProfileStatusText.Text = $"The public game data could not be saved ({ex.GetType().Name}). Diagnostic details were saved locally.";
        }
    }

    private static void WriteSyncDiagnostic(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CartLaunchCompanion", "Logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "ExophaseSyncError.log"),
                exception.ToString());
        }
        catch
        {
            // Diagnostics must never replace the original recoverable error.
        }
    }

    private static string DecodeWebMessage(string? body)
    {
        var value = body?.Trim() ?? "";
        for (var layer = 0; layer < 4; layer++)
        {
            if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
                break;

            try
            {
                value = JsonSerializer.Deserialize<string>(value)?.Trim() ?? "";
            }
            catch (JsonException)
            {
                break;
            }
        }

        return value;
    }

    private void CancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(null);
}
