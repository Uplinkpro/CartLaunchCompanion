using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PhysicalCartSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-PhysicalCartTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Identity_RoundTripsWithStableFingerprint()
    {
        Directory.CreateDirectory(_root);
        var service = new CartIdentityService();
        var created = await service.SaveNewAsync(_root, service.Create("My Game Cart"));
        var loaded = await service.LoadAsync(_root);

        Assert.Equal(created.Identity.CartId, loaded.Identity.CartId);
        Assert.Equal(created.Fingerprint, loaded.Fingerprint);
        Assert.Equal(64, loaded.Fingerprint.Length);
    }

    [Fact]
    public async Task Identity_RejectsUnknownFields()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, CartIdentityService.FileName),
            """{"FormatVersion":1,"SecurityVersion":1,"CartId":"11111111-1111-1111-1111-111111111111","DisplayName":"Cart","CreatedUtc":"2026-01-01T00:00:00Z","Execute":"cmd.exe"}""");

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new CartIdentityService().LoadAsync(_root));
    }

    [Fact]
    public async Task Identity_RejectsOversizedFile()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, CartIdentityService.FileName), new byte[CartIdentityService.MaximumBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new CartIdentityService().LoadAsync(_root));
    }

    [Fact]
    public async Task Trust_RequiresMatchingIdentityFingerprintAndExplicitAutoLaunch()
    {
        Directory.CreateDirectory(_root);
        var identityService = new CartIdentityService();
        var cart = await identityService.SaveNewAsync(_root, identityService.Create("Trusted Cart"));
        var store = new TrustedCartStore(Path.Combine(_root, "host", "trusted-carts.json"));
        await store.TrustAsync(cart, approveAutoLaunch: false);
        var database = await store.LoadAsync();

        Assert.True(TrustedCartStore.IsTrusted(database, cart));
        Assert.False(TrustedCartStore.IsTrusted(database, cart, requireAutoLaunch: true));
        var altered = cart with { Fingerprint = new string('0', 64) };
        Assert.False(TrustedCartStore.IsTrusted(database, altered));
    }

    [Fact]
    public async Task Trust_CanBeRevoked()
    {
        Directory.CreateDirectory(_root);
        var identityService = new CartIdentityService();
        var cart = await identityService.SaveNewAsync(_root, identityService.Create("Disposable Cart"));
        var store = new TrustedCartStore(Path.Combine(_root, "host", "trusted-carts.json"));
        await store.TrustAsync(cart, approveAutoLaunch: true);

        Assert.True(await store.RevokeAsync(cart.Identity.CartId));
        Assert.False(TrustedCartStore.IsTrusted(await store.LoadAsync(), cart));
    }

    [Fact]
    public void InstallationPlan_IsPerUserAndEnumeratesAllState()
    {
        var plan = CartHostInstallationPlan.ForCurrentUser();
        Assert.False(string.IsNullOrWhiteSpace(plan.InstallDirectory));
        Assert.StartsWith(plan.InstallDirectory, plan.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(plan.DataDirectory, plan.SettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(plan.DataDirectory, plan.TrustDatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(plan.DataDirectory, plan.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(plan.StartupRegistration));
    }

    [Fact]
    public void AllUsersInstallation_KeepsTrustDataPerUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        var allUsers = CartHostInstallationPlan.ForAllUsers();
        var currentUser = CartHostInstallationPlan.ForCurrentUser();
        Assert.Equal(CartHostInstallScope.AllUsers, allUsers.Scope);
        Assert.Equal(currentUser.DataDirectory, allUsers.DataDirectory, ignoreCase: true);
        Assert.NotEqual(currentUser.InstallDirectory, allUsers.InstallDirectory);
    }

    [Fact]
    public async Task InstallationService_CopiesOnlyPublishedRuntimeFiles()
    {
        var source = Path.Combine(_root, "published");
        var install = Path.Combine(_root, "installed");
        Directory.CreateDirectory(source);
        var executableName = OperatingSystem.IsWindows() ? "CLC-CartMonitor.exe" : "CLC-CartMonitor";
        await File.WriteAllTextAsync(Path.Combine(source, executableName), "host");
        await File.WriteAllTextAsync(Path.Combine(source, "dependency.dll"), "dependency");
        await File.WriteAllTextAsync(Path.Combine(source, "symbols.pdb"), "symbols");
        var data = Path.Combine(_root, "data");
        var plan = new CartHostInstallationPlan(install, data, Path.Combine(install, executableName), "startup",
            Path.Combine(data, "settings.json"), Path.Combine(data, "trusted-carts.json"), Path.Combine(data, "Logs"));

        var result = await new CartHostInstallationService().InstallFilesAsync(source, plan);

        Assert.Equal(2, result.FilesCopied);
        Assert.True(File.Exists(plan.ExecutablePath));
        Assert.True(File.Exists(Path.Combine(install, "dependency.dll")));
        Assert.False(File.Exists(Path.Combine(install, "symbols.pdb")));
    }

    [Fact]
    public void InstallationService_RemovesOnlyExplicitlySelectedUserData()
    {
        var install = Path.Combine(_root, "installed");
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(data, "Logs"));
        var plan = new CartHostInstallationPlan(install, data, Path.Combine(install, "host.exe"), "startup",
            Path.Combine(data, "settings.json"), Path.Combine(data, "trusted-carts.json"), Path.Combine(data, "Logs"));
        File.WriteAllText(plan.SettingsPath, "settings");
        File.WriteAllText(plan.TrustDatabasePath, "trust");
        File.WriteAllText(Path.Combine(plan.LogsDirectory, "host.log"), "log");

        new CartHostInstallationService().RemoveUserData(plan, removeTrust: true, removeSettings: false, removeLogs: true);

        Assert.False(File.Exists(plan.TrustDatabasePath));
        Assert.True(File.Exists(plan.SettingsPath));
        Assert.False(Directory.Exists(plan.LogsDirectory));
    }

    [Fact]
    public async Task Detector_ChecksOnlyMountRootsAndReturnsValidIdentities()
    {
        var valid = Path.Combine(_root, "valid");
        var ordinary = Path.Combine(_root, "ordinary");
        Directory.CreateDirectory(valid); Directory.CreateDirectory(ordinary);
        var identities = new CartIdentityService();
        await identities.SaveNewAsync(valid, identities.Create("Detected Cart"));
        var detector = new MountedCartDetector(new StaticMounts(valid, ordinary), identities);

        var carts = await detector.ScanAsync();

        var cart = Assert.Single(carts);
        Assert.Equal("Detected Cart", cart.Identity.Identity.DisplayName);
        Assert.Equal(Path.GetFullPath(valid), cart.MediaRoot);
    }

    [Fact]
    public async Task Detector_IgnoresMalformedCartWithoutScanningBelowRoot()
    {
        var malformed = Path.Combine(_root, "malformed");
        Directory.CreateDirectory(Path.Combine(malformed, "nested"));
        await File.WriteAllTextAsync(Path.Combine(malformed, CartIdentityService.FileName), "not json");
        await File.WriteAllTextAsync(Path.Combine(malformed, "nested", CartIdentityService.FileName), "{}");

        var carts = await new MountedCartDetector(new StaticMounts(malformed), new CartIdentityService()).ScanAsync();
        Assert.Empty(carts);
    }

    [Fact]
    public async Task Detector_SuppressesEjectedRootUntilItIsPhysicallyRemoved()
    {
        var media = Path.Combine(_root, "ejected");
        Directory.CreateDirectory(media);
        var identities = new CartIdentityService();
        await identities.SaveNewAsync(media, identities.Create("Ejected Cart"));
        var mounts = new MutableMounts();
        mounts.Set(media);
        var detector = new MountedCartDetector(mounts, identities);
        Assert.Single(await detector.ScanAsync());

        await detector.IgnoreUntilRemovedAsync(media);
        Assert.Empty(await detector.ScanAsync());

        mounts.Set();
        Assert.Empty(await detector.ScanAsync());
        mounts.Set(media);
        Assert.Single(await detector.ScanAsync());
    }

    [Fact]
    public async Task Staging_ApprovesVerifiesAndCopiesRuntimeToFixedLocalSession()
    {
        var media = await CreateRuntimeCartAsync("good runtime");
        var identities = new CartIdentityService();
        var identity = await identities.SaveNewAsync(media, identities.Create("Staging Cart"));
        var staging = new TrustedRuntimeStagingService();
        var approvals = await staging.CreateApprovalsAsync(media);
        var store = new TrustedCartStore(Path.Combine(_root, "trust", "trusted-carts.json"));
        await store.TrustAsync(identity, false, approvals);

        var prepared = await staging.PrepareAsync(media, identity, await store.LoadAsync(), "Windows-x64", Path.Combine(_root, "sessions"));

        Assert.StartsWith(Path.Combine(_root, "sessions"), prepared.SessionRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("good runtime", await File.ReadAllTextAsync(prepared.ExecutablePath));
        Assert.Equal(Path.Combine(media, "Cart"), prepared.CartRoot);
        TrustedRuntimeStagingService.DeleteSession(prepared);
        Assert.False(Directory.Exists(prepared.SessionRoot));
    }

    [Fact]
    public async Task Staging_RejectsRuntimeChangedAfterTrust()
    {
        var media = await CreateRuntimeCartAsync("approved runtime");
        var identities = new CartIdentityService();
        var identity = await identities.SaveNewAsync(media, identities.Create("Tampered Cart"));
        var staging = new TrustedRuntimeStagingService();
        var approvals = await staging.CreateApprovalsAsync(media);
        var store = new TrustedCartStore(Path.Combine(_root, "trust", "trusted-carts.json"));
        await store.TrustAsync(identity, false, approvals);
        await File.WriteAllTextAsync(Path.Combine(media, "Cart", "System", "Windows-x64", "CartLaunchCompanion.Desktop.exe"), "changed runtime");

        await Assert.ThrowsAsync<InvalidDataException>(() => staging.PrepareAsync(
            media, identity, store.LoadAsync().GetAwaiter().GetResult(), "Windows-x64", Path.Combine(_root, "sessions")));
    }

    [Fact]
    public async Task Staging_RejectsUnexpectedRuntimeFileAfterTrust()
    {
        var media = await CreateRuntimeCartAsync("approved runtime");
        var identities = new CartIdentityService();
        var identity = await identities.SaveNewAsync(media, identities.Create("Extra File Cart"));
        var staging = new TrustedRuntimeStagingService();
        var approvals = await staging.CreateApprovalsAsync(media);
        var store = new TrustedCartStore(Path.Combine(_root, "trust", "trusted-carts.json"));
        await store.TrustAsync(identity, false, approvals);
        await File.WriteAllTextAsync(Path.Combine(media, "Cart", "System", "Windows-x64", "unexpected.dll"), "surprise");

        var trust = await store.LoadAsync();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => staging.PrepareAsync(
            media, identity, trust, "Windows-x64", Path.Combine(_root, "sessions")));
        Assert.Contains("Unexpected", error.Message);
    }

    [Fact]
    public async Task Staging_RejectsUntrustedCartBeforeCreatingSession()
    {
        var media = await CreateRuntimeCartAsync("runtime");
        var identities = new CartIdentityService();
        var identity = await identities.SaveNewAsync(media, identities.Create("Untrusted Cart"));
        var sessions = Path.Combine(_root, "sessions");

        await Assert.ThrowsAsync<InvalidDataException>(() => new TrustedRuntimeStagingService().PrepareAsync(
            media, identity, new TrustedCartDatabase(), "Windows-x64", sessions));
        Assert.False(Directory.Exists(sessions) && Directory.EnumerateFileSystemEntries(sessions).Any());
    }

    [Fact]
    public void RestrictedLaunch_UsesFixedExecutableStructuredCartRootAndSanitizedEnvironment()
    {
        var session = Path.Combine(_root, "session");
        var cart = Path.Combine(_root, "media", "Cart");
        Directory.CreateDirectory(session); Directory.CreateDirectory(Path.Combine(cart, "Games"));
        var executable = Path.Combine(session, "CartLaunchCompanion.Desktop.exe");
        File.WriteAllText(executable, "runtime");
        var prepared = new PreparedCartRuntime(session, executable, cart, "Windows-x64",
            "11111111-1111-1111-1111-111111111111", new string('a', 64));
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", "untrusted-hook");
        try
        {
            var start = new PreparedCartLaunchService().CreateStartInfo(prepared);
            Assert.Equal(executable, start.FileName);
            Assert.False(start.UseShellExecute);
            Assert.Equal(["--cart-root", cart], start.ArgumentList);
            Assert.False(start.Environment.ContainsKey("DOTNET_STARTUP_HOOKS"));
            Assert.Equal(prepared.CartId, start.Environment["CLC_TRUSTED_CART_ID"]);
            Assert.Equal(prepared.RuntimeFingerprint, start.Environment["CLC_RUNTIME_FINGERPRINT"]);
        }
        finally { Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null); }
    }

    [Fact]
    public void RestrictedLaunch_RejectsExecutableOutsidePreparedSession()
    {
        var session = Path.Combine(_root, "session");
        var cart = Path.Combine(_root, "media", "Cart");
        Directory.CreateDirectory(session); Directory.CreateDirectory(cart);
        var outside = Path.Combine(_root, "CartLaunchCompanion.Desktop.exe");
        File.WriteAllText(outside, "runtime");
        var prepared = new PreparedCartRuntime(session, outside, cart, "Windows-x64",
            "11111111-1111-1111-1111-111111111111", new string('a', 64));
        Assert.Throws<InvalidDataException>(() => new PreparedCartLaunchService().CreateStartInfo(prepared));
    }

    [Fact]
    public async Task AutoLaunchPolicy_RequiresSeparateApprovalAndSuppressesDuplicates()
    {
        Directory.CreateDirectory(_root);
        var identities = new CartIdentityService();
        var cart = await identities.SaveNewAsync(_root, identities.Create("Automatic Cart"));
        var store = new TrustedCartStore(Path.Combine(_root, "trust", "trusted-carts.json"));
        await store.TrustAsync(cart, false);
        var policy = new AutomaticCartLaunchPolicy(TimeSpan.FromSeconds(30));
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(AutomaticLaunchDecision.NotApproved, policy.TryBegin(await store.LoadAsync(), cart, now));
        await store.SetAutoLaunchAsync(cart.Identity.CartId, true);
        var approved = await store.LoadAsync();
        Assert.Equal(AutomaticLaunchDecision.Allowed, policy.TryBegin(approved, cart, now));
        Assert.Equal(AutomaticLaunchDecision.AlreadyActive, policy.TryBegin(approved, cart, now.AddSeconds(1)));
        policy.Complete(cart.Identity.CartId);
        Assert.Equal(AutomaticLaunchDecision.RateLimited, policy.TryBegin(approved, cart, now.AddSeconds(5)));
        Assert.Equal(AutomaticLaunchDecision.Allowed, policy.TryBegin(approved, cart, now.AddSeconds(31)));
    }

    [Fact]
    public async Task AutoLaunchApproval_CanBeDisabledWithoutRevokingTrust()
    {
        Directory.CreateDirectory(_root);
        var identities = new CartIdentityService();
        var cart = await identities.SaveNewAsync(_root, identities.Create("Toggle Cart"));
        var store = new TrustedCartStore(Path.Combine(_root, "trust", "trusted-carts.json"));
        await store.TrustAsync(cart, true);
        Assert.True(await store.SetAutoLaunchAsync(cart.Identity.CartId, false));
        var database = await store.LoadAsync();
        Assert.True(TrustedCartStore.IsTrusted(database, cart));
        Assert.False(TrustedCartStore.IsTrusted(database, cart, requireAutoLaunch: true));
    }

    [Fact]
    public async Task Monitor_EstablishesBaselineWithoutReportingExistingCartAsInserted()
    {
        var media = Path.Combine(_root, "existing");
        Directory.CreateDirectory(media);
        var identities = new CartIdentityService();
        await identities.SaveNewAsync(media, identities.Create("Already Connected"));
        await using var monitor = new PhysicalCartMonitor(
            new MountedCartDetector(new StaticMounts(media), identities), TimeSpan.FromMilliseconds(20));
        var inserted = 0;
        monitor.CartInserted += (_, _) => Interlocked.Increment(ref inserted);
        monitor.Start();
        await Task.Delay(100);
        Assert.Equal(0, inserted);
    }

    [Fact]
    public async Task Monitor_ReportsInsertionAndRemovalExactlyOnce()
    {
        var media = Path.Combine(_root, "dynamic");
        var mounts = new MutableMounts();
        var identities = new CartIdentityService();
        await using var monitor = new PhysicalCartMonitor(
            new MountedCartDetector(mounts, identities), TimeSpan.FromMilliseconds(15));
        var inserted = 0; var removed = 0;
        monitor.CartInserted += (_, _) => Interlocked.Increment(ref inserted);
        monitor.CartRemoved += (_, _) => Interlocked.Increment(ref removed);
        monitor.Start();
        await Task.Delay(60);

        Directory.CreateDirectory(media);
        await identities.SaveNewAsync(media, identities.Create("Hotplug Cart"));
        mounts.Set(media);
        await WaitUntilAsync(() => Volatile.Read(ref inserted) == 1);
        await Task.Delay(60);
        Assert.Equal(1, inserted);

        mounts.Set();
        await WaitUntilAsync(() => Volatile.Read(ref removed) == 1);
        await Task.Delay(60);
        Assert.Equal(1, removed);
    }

    private async Task<string> CreateRuntimeCartAsync(string content)
    {
        var media = Path.Combine(_root, "media-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(media, "Cart", "System", "Windows-x64");
        Directory.CreateDirectory(runtime);
        await File.WriteAllTextAsync(Path.Combine(runtime, "CartLaunchCompanion.Desktop.exe"), content);
        await File.WriteAllTextAsync(Path.Combine(runtime, "dependency.dll"), "dependency");
        return media;
    }

    private sealed class StaticMounts(params string[] roots) : IMountRootProvider
    {
        public IEnumerable<string> GetMountedRoots() => roots;
    }

    private sealed class MutableMounts : IMountRootProvider
    {
        private string[] _roots = [];
        public IEnumerable<string> GetMountedRoots() => Volatile.Read(ref _roots);
        public void Set(params string[] roots) => Volatile.Write(ref _roots, roots);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(15, timeout.Token);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
