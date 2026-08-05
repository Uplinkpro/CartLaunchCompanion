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
    }

    private void GetSteamKeyClicked(object? sender, RoutedEventArgs e) => OpenOfficialPage(
        "https://steamcommunity.com/dev/apikey",
        "Steam’s key registration page opened. Create the key, then paste it here.");

    private void GetSteamGridDbKeyClicked(object? sender, RoutedEventArgs e) => OpenOfficialPage(
        "https://www.steamgriddb.com/profile/preferences/api",
        "SteamGridDB’s API preferences opened. Create a key, then paste it here.");

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

        _settings.SteamWebApiKey = steamKey;
        _settings.SteamGridDbApiKey = SteamGridDbKeyBox.Text?.Trim() ?? "";
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
