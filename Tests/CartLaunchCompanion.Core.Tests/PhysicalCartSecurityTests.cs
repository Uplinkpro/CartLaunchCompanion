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
        var executableName = OperatingSystem.IsWindows() ? "CartLaunchCompanion.Host.exe" : "CartLaunchCompanion.Host";
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

    private sealed class StaticMounts(params string[] roots) : IMountRootProvider
    {
        public IEnumerable<string> GetMountedRoots() => roots;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
