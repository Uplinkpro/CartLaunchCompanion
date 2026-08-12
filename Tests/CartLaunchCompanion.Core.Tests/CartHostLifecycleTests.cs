using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartHostLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-HostLifecycle-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Revocation_ImmediatelyBlocksConnectedCartPreparationAndAutomaticLaunch()
    {
        var media = await CreateRuntimeCartAsync();
        var identities = new CartIdentityService();
        var identity = await identities.SaveNewAsync(media, identities.Create("Connected cart"));
        var staging = new TrustedRuntimeStagingService();
        var approvals = await staging.CreateApprovalsAsync(media);
        var store = new TrustedCartStore(Path.Combine(_root, "data", "trusted-carts.json"));
        await store.TrustAsync(identity, approveAutoLaunch: true, approvals);
        var policy = new AutomaticCartLaunchPolicy(TimeSpan.Zero);
        var trusted = await store.LoadAsync();
        Assert.Equal(AutomaticLaunchDecision.Allowed, policy.TryBegin(trusted, identity, DateTimeOffset.UtcNow));
        policy.Complete(identity.Identity.CartId);

        Assert.True(await store.RevokeAsync(identity.Identity.CartId));
        var revoked = await store.LoadAsync();

        Assert.False(TrustedCartStore.IsTrusted(revoked, identity));
        Assert.Equal(AutomaticLaunchDecision.NotTrusted, policy.TryBegin(revoked, identity, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidDataException>(() => staging.PrepareAsync(
            media, identity, revoked, "Windows-x64", Path.Combine(_root, "sessions")));
    }

    [Fact]
    public async Task Repair_ReplacesRuntimeButPreservesEveryUserDataCategory()
    {
        var published = Path.Combine(_root, "published");
        var install = Path.Combine(_root, "runtime");
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(published);
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(Path.Combine(data, "Logs"));
        var executable = OperatingSystem.IsWindows() ? "CartLaunchCompanion.Host.exe" : "CartLaunchCompanion.Host";
        File.WriteAllText(Path.Combine(published, executable), "new runtime");
        File.WriteAllText(Path.Combine(published, "host.dll"), "new dependency");
        File.WriteAllText(Path.Combine(install, executable), "old runtime");
        var plan = Plan(install, data, executable);
        File.WriteAllText(plan.TrustDatabasePath, "trust-data");
        File.WriteAllText(plan.SettingsPath, "settings-data");
        File.WriteAllText(Path.Combine(plan.LogsDirectory, "host-audit.jsonl"), "log-data");

        await new CartHostInstallationService().InstallFilesAsync(published, plan);

        Assert.Equal("new runtime", File.ReadAllText(plan.ExecutablePath));
        Assert.Equal("new dependency", File.ReadAllText(Path.Combine(install, "host.dll")));
        Assert.Equal("trust-data", File.ReadAllText(plan.TrustDatabasePath));
        Assert.Equal("settings-data", File.ReadAllText(plan.SettingsPath));
        Assert.Equal("log-data", File.ReadAllText(Path.Combine(plan.LogsDirectory, "host-audit.jsonl")));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Uninstall_ObeysEveryRetentionCombination(bool removeTrust, bool removeSettings, bool removeLogs)
    {
        var data = Path.Combine(_root, Guid.NewGuid().ToString("N"), "data");
        var plan = Plan(Path.Combine(_root, "runtime"), data, "host.exe");
        Directory.CreateDirectory(plan.LogsDirectory);
        Directory.CreateDirectory(Path.Combine(data, "Sessions", "active"));
        File.WriteAllText(plan.TrustDatabasePath, "trust");
        File.WriteAllText(plan.SettingsPath, "settings");
        File.WriteAllText(Path.Combine(plan.LogsDirectory, "host-audit.jsonl"), "logs");
        File.WriteAllText(Path.Combine(data, "Sessions", "active", "runtime.dll"), "transient");

        new CartHostInstallationService().RemoveUserData(plan, removeTrust, removeSettings, removeLogs);

        Assert.Equal(!removeTrust, File.Exists(plan.TrustDatabasePath));
        Assert.Equal(!removeSettings, File.Exists(plan.SettingsPath));
        Assert.Equal(!removeLogs, Directory.Exists(plan.LogsDirectory));
        Assert.False(Directory.Exists(Path.Combine(data, "Sessions")));
    }

    [Fact]
    public void Uninstall_RejectsAnyUserDataPathOutsideDeclaredDataDirectory()
    {
        var data = Path.Combine(_root, "data");
        var outside = Path.Combine(_root, "outside.json");
        Directory.CreateDirectory(data); File.WriteAllText(outside, "do not delete");
        var plan = new CartHostInstallationPlan(Path.Combine(_root, "runtime"), data, "host.exe", "startup",
            outside, Path.Combine(data, "trust.json"), Path.Combine(data, "Logs"));

        Assert.Throws<InvalidDataException>(() => new CartHostInstallationService().RemoveUserData(plan, false, true, false));
        Assert.True(File.Exists(outside));
    }

    private CartHostInstallationPlan Plan(string install, string data, string executable) =>
        new(install, data, Path.Combine(install, executable), "startup", Path.Combine(data, "settings.json"),
            Path.Combine(data, "trusted-carts.json"), Path.Combine(data, "Logs"));

    private async Task<string> CreateRuntimeCartAsync()
    {
        var media = Path.Combine(_root, "media");
        var runtime = Path.Combine(media, "Cart", "System", "Windows-x64");
        Directory.CreateDirectory(runtime);
        await File.WriteAllTextAsync(Path.Combine(runtime, "CartLaunchCompanion.Desktop.exe"), "runtime");
        return media;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
