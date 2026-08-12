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
        Assert.StartsWith(plan.InstallDirectory, plan.SettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(plan.InstallDirectory, plan.TrustDatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(plan.InstallDirectory, plan.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(plan.StartupRegistration));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
