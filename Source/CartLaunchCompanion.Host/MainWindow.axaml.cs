using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CartLaunchCompanion.Core.PhysicalCarts;
using CartLaunchCompanion.Core.Portable;
using Microsoft.Win32;

namespace CartLaunchCompanion.Host;

public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private CartHostInstallationPlan _plan = CartHostInstallationPlan.ForCurrentUser();
    private TrustedCartStore _trustStore;
    private CartHostAuditLog _auditLog;
    private string _status = "Ready. Scan for connected carts, review trust, or install automatic startup from the tabs above.";
    private TrustedCartItem? _selectedCart;
    private ConnectedCartItem? _selectedConnectedCart;
    private PhysicalCartMonitor? _monitor;
    private UnpreparedCartMonitor? _setupMonitor;
    private readonly HashSet<string> _setupPromptsInProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly AutomaticCartLaunchPolicy _autoLaunchPolicy = new();
    private readonly Dictionary<string, CancellationTokenSource> _pendingAutoLaunches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PreparedCartLaunchSession> _activeLaunches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _suppressAutoLaunchUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly MountedCartDetector _cartDetector = new(new SystemMountRootProvider(), new CartIdentityService());
    private readonly UnpreparedCartDetector _unpreparedCartDetector = new(new SystemMountRootProvider());
    private readonly CartHostEjectServer _ejectServer;
    private readonly CartHostTrustReviewServer _trustReviewServer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _trustStore = new TrustedCartStore(_plan.TrustDatabasePath);
        _auditLog = new CartHostAuditLog(_plan.LogsDirectory);
        _auditLog.Write(CartHostAuditEvent.HostStarted, "started");
        _ejectServer = new CartHostEjectServer(HandleEjectRequestAsync);
        _ejectServer.Start();
        _trustReviewServer = new CartHostTrustReviewServer(HandleTrustReviewRequestAsync);
        _trustReviewServer.Start();
        Opened += async (_, _) => await RefreshTrustAsync();
        Closed += async (_, _) =>
        {
            if (_monitor is not null) await _monitor.DisposeAsync();
            if (_setupMonitor is not null) await _setupMonitor.DisposeAsync();
            await _ejectServer.DisposeAsync();
            await _trustReviewServer.DisposeAsync();
        };
    }

    public string InstallPurpose => "Runs in each signed-in user's session. It detects inserted carts, verifies only carts that user approved, and launches CLC from a protected local copy after checking every approved file.";
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
    public ConnectedCartItem? SelectedConnectedCart { get => _selectedConnectedCart; set { _selectedConnectedCart = value; Changed(); } }
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
                Status = "Windows administrator confirmation opened. Approve it to install CLC-Cart Monitor for all users.";
                return;
            }
            var result = await new CartHostInstallationService().InstallFilesAsync(AppContext.BaseDirectory, _plan);
            RegisterStartup(_plan);
            Directory.CreateDirectory(_plan.LogsDirectory);
            StartInstalledBackgroundHost(_plan);
            var promptedCarts = await PromptForConnectedTrustedCartsAsync();
            Status = $"CLC-Cart Monitor was installed or repaired for this user. {result.FilesCopied} files were copied. The Monitor will start with this user and is starting now. " +
                     (promptedCarts == 0
                         ? "Each cart will ask for its own automatic-launch approval immediately after trust."
                         : $"Automatic-launch approval was reviewed for {promptedCarts} connected trusted cart(s).");
            await Task.Delay(500);
            Close();
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
        _auditLog = new CartHostAuditLog(_plan.LogsDirectory);
        foreach (var property in new[] { nameof(ScopeDescription), nameof(InstallDirectory), nameof(DataDirectory), nameof(StartupRegistration), nameof(SettingsPath), nameof(TrustDatabasePath), nameof(LogsDirectory) }) Changed(property);
    }

    private async void TrustClicked(object? sender, RoutedEventArgs e)
    {
        var mediaRoot = SelectedConnectedCart?.MediaRoot;
        if (mediaRoot is null)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the root of a connected game cart", AllowMultiple = false });
            if (folders.Count == 0) return;
            mediaRoot = StorageItemPathResolver.Resolve(folders[0].Path);
        }
        await ReviewAndTrustAsync(mediaRoot);
    }

    private async Task ReviewAndTrustAsync(string mediaRoot)
    {
        try
        {
            Status = "Checking the cart identity and runtime before trust review…";
            var report = await new PhysicalCartReadinessService().InspectAsync(mediaRoot);
            if (!report.IsReady || report.Identity is null)
            {
                Status = "The selected media was not trusted because it did not pass cart readiness validation.";
                return;
            }
            if (!await new TrustConfirmationWindow(report, mediaRoot).ShowDialog<bool>(this))
            {
                Status = "Trust review cancelled. No permission was saved.";
                return;
            }
            await _trustStore.TrustAsync(report.Identity, approveAutoLaunch: false, report.RuntimeApprovals);
            string brandingStatus;
            try
            {
                var cachedLogo = await new TrustedCartBrandingService().CacheCollectionLogoAsync(
                    mediaRoot, report.Identity.Identity.CartId, _plan.DataDirectory);
                brandingStatus = cachedLogo is null ? "" : " Its collection logo was cached locally for verification feedback.";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                brandingStatus = " The cart remains trusted, but its collection logo could not be cached safely.";
            }
            _auditLog.Write(CartHostAuditEvent.TrustGranted, "approved", report.Identity.Identity.CartId);
            await RefreshTrustAsync();
            await ScanMountedCartsAsync();
            var automaticLaunchApproved = await PromptForAutomaticLaunchAsync(
                report.Identity.Identity.DisplayName, report.Identity.Identity.CartId);
            Status = automaticLaunchApproved
                ? $"{report.Identity.Identity.DisplayName} is trusted with {report.RuntimeApprovals.Count} approved platform runtime(s), and automatic launch is enabled for this cart.{brandingStatus}"
                : $"{report.Identity.Identity.DisplayName} is trusted with {report.RuntimeApprovals.Count} approved platform runtime(s). Automatic launch remains disabled; the cart can still be launched manually.{brandingStatus}";
        }
        catch (Exception ex) { Status = "The selected media was not trusted: " + ex.Message; }
    }

    private async void RevokeClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedCart is null) { Status = "Select a trusted cart first."; return; }
        if (await _trustStore.RevokeAsync(SelectedCart.CartId))
        {
            new TrustedCartBrandingService().RemoveCachedBranding(_plan.DataDirectory, SelectedCart.CartId);
            _auditLog.Write(CartHostAuditEvent.TrustRevoked, "revoked", SelectedCart.CartId);
            Status = $"Trust was revoked for {SelectedCart.DisplayName}. The physical cart was not changed.";
            await RefreshTrustAsync();
            await ScanMountedCartsAsync();
        }
    }

    private async void EnableAutoLaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedCart is null) { Status = "Select a trusted cart first."; return; }
        if (!await new AutoLaunchApprovalWindow(SelectedCart.DisplayName, SelectedCart.CartId).ShowDialog<bool>(this)) return;
        if (await _trustStore.SetAutoLaunchAsync(SelectedCart.CartId, true))
        {
            Status = $"Automatic launch is enabled only for {SelectedCart.DisplayName}.";
            await RefreshTrustAsync();
        }
    }

    private async Task<bool> PromptForAutomaticLaunchAsync(string displayName, string cartId)
    {
        if (!await new AutoLaunchApprovalWindow(displayName, cartId).ShowDialog<bool>(this)) return false;
        var enabled = await _trustStore.SetAutoLaunchAsync(cartId, true);
        if (enabled) await RefreshTrustAsync();
        return enabled;
    }

    private async Task<int> PromptForConnectedTrustedCartsAsync()
    {
        var database = await _trustStore.LoadAsync();
        var connected = await _cartDetector.ScanAsync();
        var prompted = 0;
        foreach (var cart in connected)
        {
            var record = database.Carts.SingleOrDefault(item =>
                item.CartId.Equals(cart.Identity.Identity.CartId, StringComparison.OrdinalIgnoreCase));
            if (record is null || record.AutoLaunchApproved || !TrustedCartStore.IsTrusted(database, cart.Identity)) continue;
            prompted++;
            await PromptForAutomaticLaunchAsync(record.DisplayName, record.CartId);
            database = await _trustStore.LoadAsync();
        }
        return prompted;
    }
    private async void DisableAutoLaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedCart is null) { Status = "Select a trusted cart first."; return; }
        if (await _trustStore.SetAutoLaunchAsync(SelectedCart.CartId, false))
        {
            Status = $"Automatic launch is disabled for {SelectedCart.DisplayName}.";
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
            var cleanupName = OperatingSystem.IsWindows() ? "CLC-CartMonitorCleanup.exe" : "CLC-CartMonitorCleanup";
            var installedCleanup = Path.Combine(_plan.InstallDirectory, cleanupName);
            if (!File.Exists(installedCleanup)) throw new FileNotFoundException("The CLC-Cart Monitor cleanup component is missing. Use Install or repair, then try again.", installedCleanup);
            var temporaryCleanup = Path.Combine(Path.GetTempPath(), $"CLC-CartMonitorCleanup-{Guid.NewGuid():N}" + Path.GetExtension(cleanupName));
            File.Copy(installedCleanup, temporaryCleanup, overwrite: false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporaryCleanup, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var cleanupProcess = Process.Start(new ProcessStartInfo
            {
                FileName = temporaryCleanup,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { Environment.ProcessId.ToString(), _plan.InstallDirectory }
            });
            if (cleanupProcess is null) throw new InvalidOperationException("The cleanup component did not start.");
            Status = "Automatic startup was removed. Selected local data was removed. Close this window to finish removing CLC-Cart Monitor; connected carts were not modified.";
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
    private async void PrepareClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedConnectedCart is null) { Status = "Select a connected cart first."; return; }
        try
        {
            Status = "Verifying the cart runtime, copying it locally, and verifying the local copy…";
            var identity = await new CartIdentityService().LoadAsync(SelectedConnectedCart.MediaRoot);
            var platform = OperatingSystem.IsWindows() ? "Windows-x64" : "Linux-x64";
            var sessions = Path.Combine(_plan.DataDirectory, "Sessions");
            var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
                SelectedConnectedCart.MediaRoot, identity, await _trustStore.LoadAsync(), platform, sessions);
            Status = $"Runtime prepared safely. Nothing was launched. Local executable: {prepared.ExecutablePath}";
        }
        catch (Exception ex) { Status = "Runtime preparation was rejected safely: " + ex.Message; }
    }
    private async void LaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (SelectedConnectedCart is null) { Status = "Select a connected cart first."; return; }
        PreparedCartRuntime? prepared = null;
        try
        {
            Status = "Verifying and preparing the selected cart before confirmation…";
            var identity = await new CartIdentityService().LoadAsync(SelectedConnectedCart.MediaRoot);
            _auditLog.Write(CartHostAuditEvent.VerificationStarted, "manual", identity.Identity.CartId);
            var platform = OperatingSystem.IsWindows() ? "Windows-x64" : "Linux-x64";
            prepared = await new TrustedRuntimeStagingService().PrepareAsync(
                SelectedConnectedCart.MediaRoot, identity, await _trustStore.LoadAsync(), platform, Path.Combine(_plan.DataDirectory, "Sessions"));
            var approved = await new LaunchConfirmationWindow(identity.Identity.DisplayName, SelectedConnectedCart.MediaRoot, prepared.ExecutablePath).ShowDialog<bool>(this);
            if (!approved) { TrustedRuntimeStagingService.DeleteSession(prepared); Status = "Launch cancelled. The prepared local session was removed."; return; }
            await new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, _trustStore);
            var session = new PreparedCartLaunchService().Start(prepared);
            _auditLog.Write(CartHostAuditEvent.VerificationAccepted, "manual", identity.Identity.CartId);
            _auditLog.Write(CartHostAuditEvent.LaunchStarted, "manual", identity.Identity.CartId);
            _activeLaunches[SelectedConnectedCart.MediaRoot] = session;
            prepared = null;
            Status = "Verified CLC is running from its protected local session. The session will be removed after it exits.";
            _ = ObserveLaunchAsync(SelectedConnectedCart.MediaRoot, session.Runtime.CartId, session);
        }
        catch (Exception ex)
        {
            _auditLog.Write(CartHostAuditEvent.VerificationRejected, ex.GetType().Name);
            if (prepared is not null) TrustedRuntimeStagingService.DeleteSession(prepared);
            Status = "Launch was rejected safely: " + ex.Message;
        }
    }
    private async Task ObserveLaunchAsync(string mediaRoot, string cartId, PreparedCartLaunchSession session)
    {
        int? exitCode = null;
        try
        {
            exitCode = await session.WaitForExitAsync();
            Status = $"Verified CLC exited with code {exitCode}. Its protected local session was removed.";
        }
        catch (Exception ex) { Status = "The verified CLC session ended unexpectedly: " + ex.Message; }
        finally { await CompleteLaunchAsync(mediaRoot, cartId, session, exitCode); }
    }
    public async Task ScanMountedCartsAsync()
    {
        try
        {
            var trusted = await _trustStore.LoadAsync();
            var detected = await _cartDetector.ScanAsync();
            ConnectedCarts.Clear();
            foreach (var cart in detected)
                ConnectedCarts.Add(new(cart.Identity.Identity.DisplayName, cart.MediaRoot, TrustedCartStore.IsTrusted(trusted, cart.Identity)));
            _auditLog.Write(CartHostAuditEvent.ScanCompleted, detected.Count == 0 ? "empty" : "found");
            Status = detected.Count == 0 ? "No physical game carts are currently detected." : $"Detected {detected.Count} physical game cart(s). Nothing was launched.";
        }
        catch (Exception ex) { Status = "Mounted-media scan stopped safely: " + ex.Message; }
    }

    public void StartPassiveMonitoring()
    {
        if (_monitor is not null) return;
        _monitor = new PhysicalCartMonitor(_cartDetector, insertionScanCompleted: (cart, elapsed) =>
            WritePerformance("detection_scan", elapsed, cart.Identity.Identity.CartId));
        _monitor.CartInserted += (_, cart) => Dispatcher.UIThread.Post(async () =>
        {
            if (_suppressAutoLaunchUntil.TryGetValue(cart.MediaRoot, out var suppressedUntil) &&
                suppressedUntil > DateTimeOffset.UtcNow)
            {
                _auditLog.Write(CartHostAuditEvent.CartInserted, "eject_remount_suppressed", cart.Identity.Identity.CartId);
                await ScanMountedCartsAsync();
                return;
            }
            _suppressAutoLaunchUntil.Remove(cart.MediaRoot);
            _auditLog.Write(CartHostAuditEvent.CartInserted, "detected", cart.Identity.Identity.CartId);
            await ScanMountedCartsAsync();
            await HandleAutomaticInsertionAsync(cart);
        });
        _monitor.CartRemoved += (_, root) => Dispatcher.UIThread.Post(async () => { _auditLog.Write(CartHostAuditEvent.CartRemoved, "detected"); await ScanMountedCartsAsync(); await HandleRemovalAsync(root); });
        _monitor.Start();

        _setupMonitor = new UnpreparedCartMonitor(_unpreparedCartDetector);
        _setupMonitor.CandidateInserted += (_, candidate) => Dispatcher.UIThread.Post(
            async () => await HandleUnpreparedInsertionAsync(candidate));
        _setupMonitor.Start();
    }

    public async Task StartBackgroundMonitoringAsync()
    {
        // A cart can already be mounted when the Monitor starts at sign-in or
        // immediately after installation. The passive monitor treats its first
        // scan as a baseline, so explicitly process that initial set once.
        var mounted = await _cartDetector.ScanAsync();
        var unprepared = _unpreparedCartDetector.Scan();
        await ScanMountedCartsAsync();
        StartPassiveMonitoring();
        foreach (var candidate in unprepared)
            await HandleUnpreparedInsertionAsync(candidate);
        foreach (var cart in mounted)
            await HandleAutomaticInsertionAsync(cart);
    }

    private async Task HandleUnpreparedInsertionAsync(UnpreparedCartCandidate candidate)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate.MediaRoot));
        if (!_setupPromptsInProgress.Add(root)) return;
        var wasVisible = IsVisible;
        try
        {
            if (File.Exists(CartIdentityService.GetIdentityPath(root))) return;
            _ = MinimizeExplorerWindowWhenAvailableAsync(root);
            _auditLog.Write(CartHostAuditEvent.SetupOffered, "runtime_detected");
            if (!wasVisible)
            {
                Show();
                Activate();
            }

            var setupWindow = new FirstInsertSetupWindow(candidate);
            if (!await setupWindow.ShowDialog<bool>(this))
            {
                _auditLog.Write(CartHostAuditEvent.SetupDeclined, "not_now");
                Status = "Cart setup was postponed. No files were changed; CLC will ask again after this drive is removed and reinserted.";
                return;
            }

            Status = "Preparing the cart identity, folders, drive branding, and runtime review…";
            var report = await new PhysicalCartReadinessService().PrepareAsync(root, setupWindow.ConfirmedName);
            if (!report.IsReady || report.Identity is null)
            {
                _auditLog.Write(CartHostAuditEvent.SetupDeclined, "readiness_failed");
                Status = "Cart setup could not be completed. Existing game content was preserved; open CLC-Cart Monitor to review the readiness checks.";
                return;
            }

            _auditLog.Write(CartHostAuditEvent.SetupAccepted, "prepared", report.Identity.Identity.CartId);
            await ScanMountedCartsAsync();
            SelectedConnectedCart = ConnectedCarts.FirstOrDefault(item =>
                Path.GetFullPath(item.MediaRoot).Equals(root, StringComparison.OrdinalIgnoreCase));
            Status = $"{report.Identity.Identity.DisplayName} is prepared. Review its verified runtime before granting trust.";
            await ReviewAndTrustAsync(root);
        }
        catch (Exception ex)
        {
            _auditLog.Write(CartHostAuditEvent.SetupDeclined, ex.GetType().Name);
            Status = "Cart setup stopped safely: " + ex.Message;
        }
        finally
        {
            _setupPromptsInProgress.Remove(root);
            if (!wasVisible) Hide();
        }
    }

    private async Task<CartHostTrustReviewResponse> HandleTrustReviewRequestAsync(CartHostTrustReviewRequest request)
    {
        try
        {
            var report = await new PhysicalCartReadinessService().InspectAsync(request.MediaRoot);
            if (!report.IsReady || report.Identity is null)
                return new(false, "The selected cart did not pass CLC-Cart Monitor readiness validation.");
            Dispatcher.UIThread.Post(async () =>
            {
                await ScanMountedCartsAsync();
                SelectedConnectedCart = ConnectedCarts.FirstOrDefault(item =>
                    Path.GetFullPath(item.MediaRoot).Equals(Path.GetFullPath(request.MediaRoot), StringComparison.OrdinalIgnoreCase));
                Show(); Activate();
                await ReviewAndTrustAsync(request.MediaRoot);
            });
            return new(true, "The cart is open in CLC-Cart Monitor for explicit trust confirmation.");
        }
        catch (Exception ex) { return new(false, "Trust review was rejected: " + ex.GetType().Name); }
    }

    public Task<CartHostTrustReviewResponse> ReviewPreparedCartAsync(string mediaRoot) =>
        HandleTrustReviewRequestAsync(new CartHostTrustReviewRequest(1, "review-trust", mediaRoot));

    private async Task HandleAutomaticInsertionAsync(DetectedPhysicalCart cart)
    {
        var totalTimer = Stopwatch.StartNew();
        CancellationTokenSource? cancellation = null;
        VerificationProgressWindow? verificationWindow = null;
        try
        {
            _ = MinimizeExplorerWindowWhenAvailableAsync(cart.MediaRoot);
            var stageTimer = Stopwatch.StartNew();
            var database = await _trustStore.LoadAsync();
            WritePerformance("trust_lookup", stageTimer.Elapsed, cart.Identity.Identity.CartId);
            var decision = _autoLaunchPolicy.TryBegin(database, cart.Identity, DateTimeOffset.UtcNow);
            if (decision != AutomaticLaunchDecision.Allowed) return;
            var cachedLogo = new TrustedCartBrandingService().GetCachedLogoPath(
                _plan.DataDirectory, cart.Identity.Identity.CartId);
            verificationWindow = new VerificationProgressWindow(cart.Identity.Identity.DisplayName, cachedLogo);
            verificationWindow.Show();
            cancellation = new CancellationTokenSource();
            _pendingAutoLaunches[cart.MediaRoot] = cancellation;
            Status = $"{cart.Identity.Identity.DisplayName} was inserted. Verifying its approved runtime for automatic launch…";
            _auditLog.Write(CartHostAuditEvent.VerificationStarted, "automatic", cart.Identity.Identity.CartId);
            var platform = OperatingSystem.IsWindows() ? "Windows-x64" : "Linux-x64";
            var prepared = await new TrustedRuntimeStagingService(stageCompleted: (stage, elapsed) =>
                WritePerformance(stage switch
                {
                    RuntimeStagingStage.SourceVerification => "source_verify",
                    RuntimeStagingStage.ProtectedCopy => "protected_copy",
                    RuntimeStagingStage.StagedVerification => "staged_verify",
                    _ => "unknown_stage"
                }, elapsed, cart.Identity.Identity.CartId)).PrepareAsync(
                cart.MediaRoot, cart.Identity, database, platform, Path.Combine(_plan.DataDirectory, "Sessions"), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Directory.Exists(cart.MediaRoot)) { TrustedRuntimeStagingService.DeleteSession(prepared); return; }
            stageTimer.Restart();
            await new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, _trustStore, cancellation.Token);
            WritePerformance("final_authorization", stageTimer.Elapsed, cart.Identity.Identity.CartId);
            verificationWindow.Close();
            verificationWindow = null;
            stageTimer.Restart();
            var session = new PreparedCartLaunchService().Start(prepared);
            WritePerformance("process_start", stageTimer.Elapsed, cart.Identity.Identity.CartId);
            _auditLog.Write(CartHostAuditEvent.VerificationAccepted, "automatic", cart.Identity.Identity.CartId);
            _auditLog.Write(CartHostAuditEvent.LaunchStarted, "automatic", cart.Identity.Identity.CartId);
            _activeLaunches[cart.MediaRoot] = session;
            WritePerformance("automatic_total", totalTimer.Elapsed, cart.Identity.Identity.CartId);
            Status = $"{cart.Identity.Identity.DisplayName} launched automatically from a verified local session.";
            _ = ObserveAutomaticLaunchAsync(cart.MediaRoot, cart.Identity.Identity.CartId, session);
        }
        catch (OperationCanceledException) { Status = "Automatic launch was cancelled because the cart was removed."; }
        catch (Exception ex)
        {
            _auditLog.Write(CartHostAuditEvent.VerificationRejected, ex.GetType().Name, cart.Identity.Identity.CartId);
            Status = "Automatic launch was rejected safely: " + ex.Message;
            if (verificationWindow is not null)
            {
                verificationWindow.ShowFailure();
                await Task.Delay(2500);
            }
        }
        finally
        {
            verificationWindow?.Close();
            if (cancellation is not null) { _pendingAutoLaunches.Remove(cart.MediaRoot); cancellation.Dispose(); }
            if (!_activeLaunches.ContainsKey(cart.MediaRoot)) _autoLaunchPolicy.Complete(cart.Identity.Identity.CartId);
        }
    }

    private void WritePerformance(string stage, TimeSpan elapsed, string? cartId = null)
    {
        var milliseconds = Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
        _auditLog.Write(CartHostAuditEvent.PerformanceStage,
            $"{stage}_ms_{milliseconds.ToString(CultureInfo.InvariantCulture)}", cartId);
    }

    private static async Task MinimizeExplorerWindowWhenAvailableAsync(string mediaRoot)
    {
        if (!OperatingSystem.IsWindows()) return;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                if (WindowsExplorerCartWindowService.TryMinimizeRootWindow(mediaRoot)) return;
            }
            catch { return; }
            await Task.Delay(200);
        }
    }

    private async Task HandleRemovalAsync(string mediaRoot)
    {
        if (_pendingAutoLaunches.Remove(mediaRoot, out var pending)) pending.Cancel();
        if (_activeLaunches.TryGetValue(mediaRoot, out var session))
        {
            Status = "The active cart was removed. Closing its verified CLC session safely…";
            try { await session.StopAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { Status = "The cart session required forced cleanup: " + ex.Message; }
        }
    }
    private async Task ObserveAutomaticLaunchAsync(string mediaRoot, string cartId, PreparedCartLaunchSession session)
    {
        int? exitCode = null;
        try { exitCode = await session.WaitForExitAsync(); }
        catch (Exception ex) { Status = "The automatic cart session ended unexpectedly: " + ex.Message; }
        finally
        {
            await CompleteLaunchAsync(mediaRoot, cartId, session, exitCode);
        }
    }

    private async Task CompleteLaunchAsync(string mediaRoot, string cartId, PreparedCartLaunchSession session, int? exitCode)
    {
        await session.DisposeAsync();
        _auditLog.Write(CartHostAuditEvent.LaunchEnded, exitCode is null ? "unknown" : $"exit_{exitCode}", cartId);
        _activeLaunches.Remove(mediaRoot);
        _autoLaunchPolicy.Complete(cartId);
    }

    private Task<CartHostEjectResponse> HandleEjectRequestAsync(CartHostEjectRequest request)
    {
        _auditLog.Write(CartHostAuditEvent.EjectRequested, "received", request.CartId);
        var match = _activeLaunches.FirstOrDefault(item =>
            item.Value.Runtime.CartId.Equals(request.CartId, StringComparison.OrdinalIgnoreCase));
        if (match.Value is null)
        {
            _auditLog.Write(CartHostAuditEvent.EjectRejected, "no_active_session", request.CartId);
            return Task.FromResult(new CartHostEjectResponse(false, "No active verified session matches this trusted cart."));
        }
        _auditLog.Write(CartHostAuditEvent.EjectAccepted, "matched", request.CartId);
        Dispatcher.UIThread.Post(async () => await StopAndEjectAsync(match.Key, request.CartId, match.Value));
        return Task.FromResult(new CartHostEjectResponse(true, "Safe removal accepted."));
    }

    private async Task StopAndEjectAsync(string mediaRoot, string cartId, PreparedCartLaunchSession session)
    {
        Status = "Closing the verified launcher and preparing the cart for safe removal…";
        _suppressAutoLaunchUntil[mediaRoot] = DateTimeOffset.MaxValue;
        var trust = await _trustStore.LoadAsync();
        var cartName = trust.Carts.FirstOrDefault(item =>
            item.CartId.Equals(cartId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? "Game cart";
        var cachedLogo = new TrustedCartBrandingService().GetCachedLogoPath(_plan.DataDirectory, cartId);
        EjectProgressWindow? ejectProgress = new(cartName, cachedLogo);
        ejectProgress.Show();
        ejectProgress.Activate();
        // Give Avalonia one render opportunity before beginning filesystem and
        // device work so feedback appears immediately after the user's click.
        await Task.Delay(75);
        try
        {
            await _cartDetector.IgnoreUntilRemovedAsync(mediaRoot);
            await session.StopAsync(TimeSpan.FromSeconds(5));
            ejectProgress.UpdateDetail("The launcher is closed. Waiting for Windows to release the cart…");
            for (var attempt = 0; attempt < 50 && _activeLaunches.ContainsKey(mediaRoot); attempt++)
                await Task.Delay(100);
            if (OperatingSystem.IsWindows() && WindowsExplorerCartWindowService.TryCloseRootWindow(mediaRoot))
                await Task.Delay(700);

            while (true)
            {
                try
                {
                    ejectProgress.UpdateDetail("Flushing writes and requesting safe removal from Windows…");
                    var outcome = await EjectWithRetriesAsync(mediaRoot, cartId);
                    _suppressAutoLaunchUntil[mediaRoot] = DateTimeOffset.UtcNow.AddSeconds(5);
                    Status = outcome == SafeMediaEjectOutcome.Ejected
                        ? "The cart was safely ejected and can now be removed."
                        : "The cart was already removed. Its verified local session was closed safely.";
                    _auditLog.Write(outcome == SafeMediaEjectOutcome.Ejected ? CartHostAuditEvent.EjectCompleted : CartHostAuditEvent.EjectAlreadyRemoved, "complete", cartId);
                    ejectProgress?.Close();
                    ejectProgress = null;
                    await ShowEjectResultAsync(
                        success: true,
                        cartName,
                        outcome == SafeMediaEjectOutcome.Ejected
                            ? "Windows confirmed that the cart is no longer mounted. You can unplug the physical drive now."
                            : "The cart was already disconnected. Its protected local launcher session closed safely.",
                        cachedLogo);
                    return;
                }
                catch (Exception ex)
                {
                    _suppressAutoLaunchUntil[mediaRoot] = DateTimeOffset.UtcNow.AddSeconds(30);
                    _cartDetector.RestoreRoot(mediaRoot);
                    _auditLog.Write(CartHostAuditEvent.EjectFailed, $"{ex.GetType().Name}_{ex.Message}", cartId);
                    Status = "The cart remains mounted because safe removal failed: " + ex.Message;
                    ejectProgress?.Close();
                    ejectProgress = null;
                    var action = await ShowEjectResultAsync(
                        success: false,
                        cartName,
                        "The cart is still mounted and must not be unplugged. " + ex.Message +
                        " Close any program using the cart, then retry safe removal.",
                        cachedLogo);
                    if (action == EjectResultAction.Retry)
                    {
                        _suppressAutoLaunchUntil[mediaRoot] = DateTimeOffset.MaxValue;
                        await _cartDetector.IgnoreUntilRemovedAsync(mediaRoot);
                        if (OperatingSystem.IsWindows()) WindowsExplorerCartWindowService.TryCloseRootWindow(mediaRoot);
                        ejectProgress = new EjectProgressWindow(cartName, cachedLogo);
                        ejectProgress.Show();
                        ejectProgress.Activate();
                        ejectProgress.UpdateDetail("Retrying safe removal. Do not unplug the cart yet…");
                        await Task.Delay(75);
                        continue;
                    }
                    if (action == EjectResultAction.Reopen)
                        await ReopenVerifiedCartAsync(mediaRoot, cartId);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _suppressAutoLaunchUntil[mediaRoot] = DateTimeOffset.UtcNow.AddSeconds(30);
            _cartDetector.RestoreRoot(mediaRoot);
            _auditLog.Write(CartHostAuditEvent.EjectFailed, $"{ex.GetType().Name}_{ex.Message}", cartId);
            Status = "The cart was closed but safe removal could not start: " + ex.Message;
            ejectProgress?.Close();
            ejectProgress = null;
            await ShowEjectResultAsync(
                success: false,
                cartName,
                "The cart is still mounted and must not be unplugged. " + ex.Message,
                cachedLogo);
        }
        finally
        {
            ejectProgress?.Close();
            _autoLaunchPolicy.Complete(cartId);
        }
    }

    private static async Task<SafeMediaEjectOutcome> EjectWithRetriesAsync(string mediaRoot, string cartId)
    {
        const int maximumAttempts = 5;
        var ejector = new SafeMediaEjectService();
        for (var attempt = 1; ; attempt++)
        {
            try { return await ejector.EjectAsync(mediaRoot, cartId); }
            catch (IOException) when (attempt < maximumAttempts)
            {
                if (OperatingSystem.IsWindows()) WindowsExplorerCartWindowService.TryCloseRootWindow(mediaRoot);
                // Windows can briefly remount or retain a handle after the
                // launcher exits. Give Plug and Play progressively more time
                // to settle while the progress window remains visible.
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 4)));
            }
        }
    }

    private static async Task<EjectResultAction> ShowEjectResultAsync(
        bool success,
        string cartName,
        string detail,
        string? cachedLogo)
    {
        var result = new EjectResultWindow(success, cartName, detail, cachedLogo);
        result.Show();
        result.Activate();
        return await result.Completion;
    }

    private async Task ReopenVerifiedCartAsync(string mediaRoot, string cartId)
    {
        PreparedCartRuntime? prepared = null;
        try
        {
            var identity = await new CartIdentityService().LoadAsync(mediaRoot);
            if (!identity.Identity.CartId.Equals(cartId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The mounted media no longer matches the cart that failed to eject.");
            var platform = OperatingSystem.IsWindows() ? "Windows-x64" : "Linux-x64";
            prepared = await new TrustedRuntimeStagingService().PrepareAsync(
                mediaRoot, identity, await _trustStore.LoadAsync(), platform, Path.Combine(_plan.DataDirectory, "Sessions"));
            await new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, _trustStore);
            var reopened = new PreparedCartLaunchService().Start(prepared);
            _auditLog.Write(CartHostAuditEvent.VerificationAccepted, "eject_reopen", cartId);
            _auditLog.Write(CartHostAuditEvent.LaunchStarted, "eject_reopen", cartId);
            _activeLaunches[mediaRoot] = reopened;
            prepared = null;
            Status = "The verified launcher reopened after the failed eject attempt.";
            _ = ObserveAutomaticLaunchAsync(mediaRoot, cartId, reopened);
        }
        catch (Exception ex)
        {
            if (prepared is not null) TrustedRuntimeStagingService.DeleteSession(prepared);
            Status = "CLC could not be reopened safely: " + ex.Message;
            Show();
            Activate();
        }
    }

    private static void RegisterStartup(CartHostInstallationPlan plan)
    {
        if (OperatingSystem.IsWindows())
        {
            var hive = plan.Scope == CartHostInstallScope.AllUsers ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key.SetValue("CLCCartMonitor", $"\"{plan.ExecutablePath}\" --background", RegistryValueKind.String);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(plan.StartupRegistration)!);
        File.WriteAllText(plan.StartupRegistration, $"[Desktop Entry]\nType=Application\nName=CLC-Cart Monitor\nExec=\"{plan.ExecutablePath}\" --background\nX-GNOME-Autostart-enabled=true\n");
    }

    private static void StartInstalledBackgroundHost(CartHostInstallationPlan plan)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = plan.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--background", "--wait-for-process", Environment.ProcessId.ToString() }
        });
        if (process is null) throw new InvalidOperationException("The installed CLC-Cart Monitor did not start.");
    }

    private static void RemoveStartup(CartHostInstallationPlan plan)
    {
        if (OperatingSystem.IsWindows())
        {
            var hive = plan.Scope == CartHostInstallScope.AllUsers ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("CLCCartMonitor", throwOnMissingValue: false);
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
