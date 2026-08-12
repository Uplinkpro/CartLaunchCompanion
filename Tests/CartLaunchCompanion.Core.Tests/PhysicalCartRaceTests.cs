using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PhysicalCartRaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-RaceTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Staging_CancellationDuringCopyRemovesIncompleteSession()
    {
        var setup = await CreateTrustedCartAsync(extraFiles: 8);
        using var cancellation = new CancellationTokenSource();
        var staging = new TrustedRuntimeStagingService(_ => cancellation.Cancel());
        var sessions = Path.Combine(_root, "sessions");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => staging.PrepareAsync(
            setup.Media, setup.Identity, setup.Database, "Windows-x64", sessions, cancellation.Token));

        Assert.False(Directory.Exists(sessions) && Directory.EnumerateFileSystemEntries(sessions).Any());
    }

    [Fact]
    public async Task FinalAuthorization_RejectsTrustRevokedAfterStaging()
    {
        var setup = await CreateTrustedCartAsync();
        var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
            setup.Media, setup.Identity, setup.Database, "Windows-x64", Path.Combine(_root, "sessions"));
        await setup.Store.RevokeAsync(setup.Identity.Identity.CartId);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, setup.Store));
        TrustedRuntimeStagingService.DeleteSession(prepared);
    }

    [Fact]
    public async Task FinalAuthorization_RejectsCartRemovedAfterStaging()
    {
        var setup = await CreateTrustedCartAsync();
        var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
            setup.Media, setup.Identity, setup.Database, "Windows-x64", Path.Combine(_root, "sessions"));
        Directory.Delete(Path.Combine(setup.Media, "Cart"), true);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, setup.Store));
        Assert.Contains("removed", error.Message, StringComparison.OrdinalIgnoreCase);
        TrustedRuntimeStagingService.DeleteSession(prepared);
    }

    [Fact]
    public async Task FinalAuthorization_RejectsIdentitySubstitutionAfterStaging()
    {
        var setup = await CreateTrustedCartAsync();
        var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
            setup.Media, setup.Identity, setup.Database, "Windows-x64", Path.Combine(_root, "sessions"));
        File.Delete(Path.Combine(setup.Media, CartIdentityService.FileName));
        var identities = new CartIdentityService();
        await identities.SaveNewAsync(setup.Media, identities.Create("Replacement"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, setup.Store));
        TrustedRuntimeStagingService.DeleteSession(prepared);
    }

    [Fact]
    public async Task FinalAuthorization_RejectsRuntimeApprovalChangedAfterStaging()
    {
        var setup = await CreateTrustedCartAsync();
        var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
            setup.Media, setup.Identity, setup.Database, "Windows-x64", Path.Combine(_root, "sessions"));
        var changedApprovals = await new TrustedRuntimeStagingService().CreateApprovalsAsync(setup.Media);
        changedApprovals[0].RootFingerprint = new string('a', 64);
        changedApprovals[0].Files[0].Sha256 = new string('a', 64);
        changedApprovals[0].RootFingerprint = CartLaunchCompanion.Core.Updating.RuntimeIntegrityVerifier.ComputeRootFingerprint(changedApprovals[0].Files);
        await setup.Store.TrustAsync(setup.Identity, true, changedApprovals);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, setup.Store));
        TrustedRuntimeStagingService.DeleteSession(prepared);
    }

    [Fact]
    public async Task FinalAuthorization_AcceptsUnchangedConnectedTrustedCart()
    {
        var setup = await CreateTrustedCartAsync();
        var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
            setup.Media, setup.Identity, setup.Database, "Windows-x64", Path.Combine(_root, "sessions"));

        await new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, setup.Store);
        TrustedRuntimeStagingService.DeleteSession(prepared);
    }

    [Fact]
    public async Task FinalAuthorization_RejectsStagedRuntimeTamperingImmediatelyBeforeLaunch()
    {
        var setup = await CreateTrustedCartAsync();
        var prepared = await new TrustedRuntimeStagingService().PrepareAsync(
            setup.Media, setup.Identity, setup.Database, "Windows-x64", Path.Combine(_root, "sessions"));
        await File.WriteAllTextAsync(prepared.ExecutablePath, "tampered after staging");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PreparedCartAuthorizationService().ValidateImmediatelyBeforeLaunchAsync(prepared, setup.Store));
        TrustedRuntimeStagingService.DeleteSession(prepared);
    }

    private async Task<Setup> CreateTrustedCartAsync(int extraFiles = 0)
    {
        var media = Path.Combine(_root, "media-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(media, "Cart", "System", "Windows-x64");
        Directory.CreateDirectory(runtime);
        await File.WriteAllTextAsync(Path.Combine(runtime, "CartLaunchCompanion.Desktop.exe"), "runtime");
        for (var index = 0; index < extraFiles; index++)
            await File.WriteAllBytesAsync(Path.Combine(runtime, $"dependency-{index}.bin"), new byte[64 * 1024]);
        var identities = new CartIdentityService();
        var identity = await identities.SaveNewAsync(media, identities.Create("Race cart"));
        var staging = new TrustedRuntimeStagingService();
        var approvals = await staging.CreateApprovalsAsync(media);
        var store = new TrustedCartStore(Path.Combine(_root, "trust-" + Guid.NewGuid().ToString("N"), "trusted-carts.json"));
        await store.TrustAsync(identity, true, approvals);
        return new(media, identity, store, await store.LoadAsync());
    }

    private sealed record Setup(string Media, VerifiedCartIdentity Identity, TrustedCartStore Store, TrustedCartDatabase Database);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
