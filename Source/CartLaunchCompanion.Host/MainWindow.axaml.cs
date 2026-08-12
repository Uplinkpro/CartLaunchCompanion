using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CartLaunchCompanion.Core.PhysicalCarts;
using Microsoft.Win32;

namespace CartLaunchCompanion.Host;

public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private CartHostInstallationPlan _plan = CartHostInstallationPlan.ForCurrentUser();
    private TrustedCartStore _trustStore;
    private string _status = "Drive monitoring and automatic launch are not enabled in this milestone.";
    private TrustedCartItem? _selectedCart;
    private PhysicalCartMonitor? _monitor;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _trustStore = new TrustedCartStore(_plan.TrustDatabasePath);
        Opened += async (_, _) => await RefreshTrustAsync();
    }

    public string InstallPurpose => "Runs only for your signed-in account. It will eventually detect inserted carts, verify carts you approved, and stage verified CLC runtime files locally before launch.";
    public bool ShowWindowsScope => OperatingSystem.IsWindows();
    public string ScopeDescription => _plan.Scope == CartHostInstallScope.AllUsers ? "All Windows users (administrator approval required)" : "Current signed-in user only";
    public string InstallDirectory => _plan.InstallDirectory;
    public string DataDirectory => _plan.DataDirectory;
    public string StartupRegistration => _plan.StartupRegistration;
    public string SettingsPath => _plan.SettingsPath;
    public string TrustDatabasePath => _plan.TrustDatabasePath;
    public string LogsDirectory => _plan.LogsDirectory;
    public ObservableCollection<TrustedCartItem> TrustedCarts { get; } = [];
    public ObservableCollection<ConnectedCartItem> ConnectedCarts { get; } = [];
    public TrustedCartItem? SelectedCart { get => _selectedCart; set { _selectedCart = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public new event PropertyChangedEventHandler? PropertyChanged;

    private async void InstallClicked(object? sender, RoutedEventArgs e)
    {
        if (InstallConfirm.IsChecked != true) { Status = "Installation was not started. Check the confirmation after reviewing every path."; return; }
        try
        {
            if (_plan.Scope == CartHostInstallScope.AllUsers)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true,
                    Verb = "runas",
                    ArgumentList = { "--install-all-users" }
                });
                Status = "Windows administrator confirmation opened. Approve it to install the Host for all users.";
                return;
            }
            var result = await new CartHostInstallationService().InstallFilesAsync(AppContext.BaseDirectory, _plan);
            RegisterStartup(_plan);
            Directory.CreateDirectory(_plan.LogsDirectory);
            Status = $"Cart Launch Host installed or repaired for this user. {result.FilesCopied} runtime files were copied. No administrator access was used.";
        }
        catch (Exception ex) { Status = "Installation stopped safely: " + ex.Message; }
    }

    private void CurrentUserScopeChecked(object? sender, RoutedEventArgs e) => ChangeScope(CartHostInstallationPlan.ForCurrentUser());
    private void AllUsersScopeChecked(object? sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsWindows()) ChangeScope(CartHostInstallationPlan.ForAllUsers());
    }
    private void ChangeScope(CartHostInstallationPlan plan)
    {
        _plan = plan;
        _trustStore = new TrustedCartStore(_plan.TrustDatabasePath);
        foreach (var property in new[] { nameof(ScopeDescription), nameof(InstallDirectory), nameof(DataDirectory), nameof(StartupRegistration), nameof(SettingsPath), nameof(TrustDatabasePath), nameof(LogsDirectory) }) Changed(property);
    }

    private async void TrustClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the root of a connected game cart", AllowMultiple = false });
        if (folders.Count == 0) return;
        try
        {
            var identity = await new CartIdentityService().LoadAsync(folders[0].Path.LocalPath);
            await _trustStore.TrustAsync(identity, approveAutoLaunch: false);
            await RefreshTrustAsync();
            Status = $"{identity.Identity.DisplayName} is trusted on this computer. Automatic launch remains disabled.";
        }
        catch (Exception ex) { Status = "The selected media was not trusted: " + ex.Message; }
    }

    private async void RevokeClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedCart is null) { Status = "Select a trusted cart first."; return; }
        if (await _trustStore.RevokeAsync(SelectedCart.CartId))
        {
            Status = $"Trust was revoked for {SelectedCart.DisplayName}. The physical cart was not changed.";
            await RefreshTrustAsync();
        }
    }

    private void UninstallClicked(object? sender, RoutedEventArgs e)
    {
        if (UninstallConfirm.IsChecked != true) { Status = "Uninstall was not started. Check the confirmation after choosing what data to remove."; return; }
        try
        {
            RemoveStartup(_plan);
            new CartHostInstallationService().RemoveUserData(_plan, RemoveTrust.IsChecked == true, RemoveSettings.IsChecked == true, RemoveLogs.IsChecked == true);
            var cleanupName = OperatingSystem.IsWindows() ? "CartLaunchCompanion.HostCleanup.exe" : "CartLaunchCompanion.HostCleanup";
            var installedCleanup = Path.Combine(_plan.InstallDirectory, cleanupName);
            if (!File.Exists(installedCleanup)) throw new FileNotFoundException("The Host cleanup component is missing. Use Install or repair, then try again.", installedCleanup);
            var temporaryCleanup = Path.Combine(Path.GetTempPath(), $"CLC-HostCleanup-{Guid.NewGuid():N}" + Path.GetExtension(cleanupName));
            File.Copy(installedCleanup, temporaryCleanup, overwrite: false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporaryCleanup, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Process.Start(new ProcessStartInfo
            {
                FileName = temporaryCleanup,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { Environment.ProcessId.ToString(), _plan.InstallDirectory }
            }) ?? throw new InvalidOperationException("The cleanup component did not start.");
            Status = "Automatic startup was removed. Selected local data was removed. Close this window to finish removing the local Host runtime; connected carts were not modified.";
            Close();
        }
        catch (Exception ex) { Status = "Uninstall stopped safely: " + ex.Message; }
    }

    private async Task RefreshTrustAsync()
    {
        try
        {
            var database = await _trustStore.LoadAsync();
            TrustedCarts.Clear();
            foreach (var record in database.Carts.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
                TrustedCarts.Add(new(record.DisplayName, record.CartId, record.AutoLaunchApproved));
        }
        catch (Exception ex) { Status = "Trusted-cart records could not be loaded safely: " + ex.Message; }
    }

    private async void ScanClicked(object? sender, RoutedEventArgs e) => await ScanMountedCartsAsync();
    public async Task ScanMountedCartsAsync()
    {
        try
        {
            var trusted = await _trustStore.LoadAsync();
            var detected = await new MountedCartDetector(new SystemMountRootProvider(), new CartIdentityService()).ScanAsync();
            ConnectedCarts.Clear();
            foreach (var cart in detected)
                ConnectedCarts.Add(new(cart.Identity.Identity.DisplayName, cart.MediaRoot, TrustedCartStore.IsTrusted(trusted, cart.Identity)));
            Status = detected.Count == 0 ? "No physical game carts are currently detected." : $"Detected {detected.Count} physical game cart(s). Nothing was launched.";
        }
        catch (Exception ex) { Status = "Mounted-media scan stopped safely: " + ex.Message; }
    }

    public void StartPassiveMonitoring()
    {
        if (_monitor is not null) return;
        _monitor = new PhysicalCartMonitor(new MountedCartDetector(new SystemMountRootProvider(), new CartIdentityService()));
        _monitor.CartInserted += async (_, _) => await ScanMountedCartsAsync();
        _monitor.CartRemoved += async (_, _) => await ScanMountedCartsAsync();
        _monitor.Start();
    }

    private static void RegisterStartup(CartHostInstallationPlan plan)
    {
        if (OperatingSystem.IsWindows())
        {
            var hive = plan.Scope == CartHostInstallScope.AllUsers ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key.SetValue("CartLaunchCompanionHost", $"\"{plan.ExecutablePath}\" --background", RegistryValueKind.String);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(plan.StartupRegistration)!);
        File.WriteAllText(plan.StartupRegistration, $"[Desktop Entry]\nType=Application\nName=Cart Launch Host\nExec=\"{plan.ExecutablePath}\" --background\nX-GNOME-Autostart-enabled=true\n");
    }

    private static void RemoveStartup(CartHostInstallationPlan plan)
    {
        if (OperatingSystem.IsWindows())
        {
            var hive = plan.Scope == CartHostInstallScope.AllUsers ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("CartLaunchCompanionHost", throwOnMissingValue: false);
        }
        else if (File.Exists(plan.StartupRegistration)) File.Delete(plan.StartupRegistration);
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record TrustedCartItem(string DisplayName, string CartId, bool AutoLaunchApproved)
{
    public string ApprovalText => AutoLaunchApproved ? "Trusted • automatic launch approved" : "Trusted • automatic launch disabled";
}

public sealed record ConnectedCartItem(string DisplayName, string MediaRoot, bool IsTrusted)
{
    public string TrustText => IsTrusted ? "Trusted on this computer" : "Not trusted — no launch permission";
}
