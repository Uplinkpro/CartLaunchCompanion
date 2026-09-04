using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CartLaunchCompanion.Configurator;

public sealed partial class ApiSetupDialog : Window
{
    private readonly ConfiguratorSettings _settings;

    public ApiSetupDialog()
    {
        _settings = new ConfiguratorSettings();
        InitializeComponent();
    }

    public ApiSetupDialog(ConfiguratorSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        SteamKeyBox.Text = settings.SteamWebApiKey;
        SteamGridDbKeyBox.Text = settings.SteamGridDbApiKey;
        RetroAchievementsUserBox.Text = settings.RetroAchievementsUserName;
        RetroAchievementsKeyBox.Text = settings.RetroAchievementsWebApiKey;
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
        _settings.SteamWebApiKey = steamKey;
        _settings.SteamGridDbApiKey = steamGridDbKey;
        _settings.RetroAchievementsUserName = retroAchievementsUser;
        _settings.RetroAchievementsWebApiKey = retroAchievementsKey;
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
