using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed partial class ApiSetupDialog : Window
{
    private readonly ConfiguratorSettings _settings;
    private readonly IReadOnlyCollection<string> _cartTitles;

    public ApiSetupDialog()
    {
        _settings = new ConfiguratorSettings();
        _cartTitles = [];
        InitializeComponent();
        Opened += FitToOwnerWorkingArea;
    }

    public ApiSetupDialog(ConfiguratorSettings settings, IReadOnlyCollection<string>? cartTitles = null)
    {
        _settings = settings;
        _cartTitles = cartTitles ?? [];
        InitializeComponent();
        Opened += FitToOwnerWorkingArea;
        SteamKeyBox.Text = settings.SteamWebApiKey;
        SteamGridDbKeyBox.Text = settings.SteamGridDbApiKey;
        RetroAchievementsUserBox.Text = settings.RetroAchievementsUserName;
        RetroAchievementsKeyBox.Text = settings.RetroAchievementsWebApiKey;
        ExophaseProfileBox.Text = settings.ExophasePlayerId;
        SteamGridDbKeyStatus.Text = string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey)
            ? "No SteamGridDB key is currently stored."
            : $"Stored SteamGridDB value: {settings.SteamGridDbApiKey.Length} characters. Paste the complete key if this looks too short.";
    }

    private void GetSteamKeyClicked(object? sender, RoutedEventArgs e) => OpenOfficialPage(
        "https://steamcommunity.com/dev/apikey",
        "Steam’s key registration page opened. Create the key, then paste it here.");

    private void GetSteamGridDbKeyClicked(object? sender, RoutedEventArgs e) => OpenOfficialPage(
        "https://www.steamgriddb.com/profile/preferences/api",
        "SteamGridDB’s API preferences opened. Create a key, then paste it here.");

    private void GetRetroAchievementsKeyClicked(object? sender, RoutedEventArgs e) => OpenOfficialPage(
        "https://retroachievements.org/controlpanel.php",
        "RetroAchievements’ control panel opened. Copy the Web API key from the Keys section, then paste it here.");

    private async void ConnectExophaseClicked(object? sender, RoutedEventArgs e)
    {
        var value = ExophaseProfileBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusText.Text = "Enter your public Exophase profile URL or username first.";
            return;
        }

        var playerId = await ResolveExophaseWithBrowserAsync(value);
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            ExophaseProfileBox.Text = playerId;
            StatusText.Text = "Exophase connected. The public player ID was found automatically.";
        }
    }

    private string GetExophaseProfileUrl()
    {
        var value = ExophaseProfileBox.Text?.Trim() ?? "";
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Host.EndsWith("exophase.com", StringComparison.OrdinalIgnoreCase))
            return uri.ToString();
        if (!string.IsNullOrWhiteSpace(value) && value.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_'))
            return $"https://www.exophase.com/user/{Uri.EscapeDataString(value)}/";
        return "https://www.exophase.com/";
    }

    private async Task<string?> ResolveExophaseWithBrowserAsync(string value)
    {
        if (value.All(char.IsDigit))
        {
            try
            {
                return await new ExophaseProfileDialog("https://www.exophase.com/", value, _cartTitles)
                    .ShowDialog<string?>(this);
            }
            catch (Exception ex)
            {
                StatusText.Text = "The browser helper could not open. You can retry after installing the system WebView support. " + ex.Message;
                return null;
            }
        }

        try
        {
            return await new ExophaseProfileDialog(GetExophaseProfileUrl(), cartTitles: _cartTitles).ShowDialog<string?>(this);
        }
        catch (Exception ex)
        {
            StatusText.Text = "The browser helper could not open. You can retry after installing the system WebView support. " + ex.Message;
            return null;
        }
    }

    private void FitToOwnerWorkingArea(object? sender, EventArgs e)
    {
        const double edgeRoom = 48;
        var availableWidth = Owner?.ClientSize.Width ?? 760;
        var availableHeight = Owner?.ClientSize.Height ?? 640;
        Width = Math.Max(MinWidth, Math.Min(760, availableWidth - edgeRoom));
        Height = Math.Max(MinHeight, Math.Min(640, availableHeight - edgeRoom));
    }

    private void OpenOfficialPage(string url, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            StatusText.Text = message;
        }
        catch (Exception ex) { StatusText.Text = "Could not open the browser: " + ex.Message; }
    }

    private async void SaveClicked(object? sender, RoutedEventArgs e)
    {
        var steamKey = SteamKeyBox.Text?.Trim() ?? "";
        if (steamKey.Length == 0)
        {
            StatusText.Text = "Paste a Steam Web API key, or choose Continue offline.";
            return;
        }

        var steamGridDbKey = SteamGridDbKeyBox.Text?.Trim() ?? "";
        if (steamGridDbKey.Length is > 0 and < 20)
        {
            StatusText.Text = $"The SteamGridDB value is only {steamGridDbKey.Length} characters and appears incomplete. Paste the full key, or clear the field to disable SteamGridDB.";
            return;
        }
        var retroAchievementsUser = RetroAchievementsUserBox.Text?.Trim() ?? "";
        var retroAchievementsKey = RetroAchievementsKeyBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(retroAchievementsUser) != string.IsNullOrWhiteSpace(retroAchievementsKey))
        {
            StatusText.Text = "RetroAchievements requires both the username and Web API key. Complete both fields, or clear both to disable it.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(retroAchievementsKey))
        {
            StatusText.Text = "Checking the RetroAchievements account…";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            if (!await new CartLaunchCompanion.Core.Metadata.RetroAchievementsClient(client)
                    .ValidateAccountAsync(retroAchievementsUser, retroAchievementsKey))
            {
                StatusText.Text = "RetroAchievements rejected that username or Web API key. Check both values in the account control panel.";
                return;
            }
        }
        var exophaseValue = ExophaseProfileBox.Text?.Trim() ?? "";
        var exophasePlayerId = "";
        if (!string.IsNullOrWhiteSpace(exophaseValue))
        {
            StatusText.Text = "Checking the public Exophase profile…";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            exophasePlayerId = await new CartLaunchCompanion.Core.Metadata.ExophaseClient(client)
                .ResolvePlayerIdAsync(exophaseValue) ?? "";
            if (string.IsNullOrWhiteSpace(exophasePlayerId))
            {
                exophasePlayerId = await ResolveExophaseWithBrowserAsync(exophaseValue) ?? "";
                if (string.IsNullOrWhiteSpace(exophasePlayerId))
                {
                    StatusText.Text = "Exophase was not connected. Complete its browser check and wait for the public profile to load, then try again.";
                    return;
                }
                ExophaseProfileBox.Text = exophasePlayerId;
            }
        }
        _settings.SteamWebApiKey = steamKey;
        _settings.SteamGridDbApiKey = steamGridDbKey;
        _settings.RetroAchievementsUserName = retroAchievementsUser;
        _settings.RetroAchievementsWebApiKey = retroAchievementsKey;
        _settings.ExophasePlayerId = exophasePlayerId;
        _settings.SetupCompleted = true;
        await _settings.SaveAsync();
        Close(true);
    }

    private async void OfflineClicked(object? sender, RoutedEventArgs e)
    {
        _settings.SetupCompleted = true;
        await _settings.SaveAsync();
        Close(true);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

}
