using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record PhysicalCartReadinessCheck(string Name, bool Passed, string Detail);
public sealed record PhysicalCartReadinessReport(
    bool IsReady, bool HasIdentity, VerifiedCartIdentity? Identity,
    IReadOnlyList<TrustedRuntimeApproval> RuntimeApprovals,
    IReadOnlyList<PhysicalCartReadinessCheck> Checks);

public sealed class PhysicalCartReadinessService
{
    public async Task<PhysicalCartReadinessReport> InspectAsync(
        string mediaRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        var checks = new List<PhysicalCartReadinessCheck>();
        foreach (var folder in new[] { "Cart", "Games", "Emulators", "Roms" })
        {
            var exists = Directory.Exists(Path.Combine(root, folder));
            checks.Add(new($"{folder} folder", exists, exists ? "Present" : $"Create {folder} at the media root."));
        }

        VerifiedCartIdentity? identity = null;
        try
        {
            identity = await new CartIdentityService().LoadAsync(root, cancellationToken);
            checks.Add(new("Cart identity", true, $"Valid identity for {identity.Identity.DisplayName}"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            checks.Add(new("Cart identity", false, File.Exists(CartIdentityService.GetIdentityPath(root))
                ? "The existing identity is invalid and was not changed."
                : "No identity has been created yet."));
        }

        IReadOnlyList<TrustedRuntimeApproval> approvals = [];
        if (Directory.Exists(Path.Combine(root, "Cart")))
        {
            try
            {
                approvals = await new TrustedRuntimeStagingService().CreateApprovalsAsync(root, cancellationToken);
                foreach (var platform in new[] { "Windows-x64", "Linux-x64" })
                {
                    var approval = approvals.SingleOrDefault(item => item.Platform == platform);
                    checks.Add(new($"{platform} runtime", approval is not null,
                        approval is null ? "Not included (optional unless this platform is required)." : $"Verified {approval.Files.Count} runtime files."));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                checks.Add(new("CLC runtime", false, "Runtime verification failed: " + ex.Message));
            }
        }

        var essential = checks.Where(item => item.Name is "Cart folder" or "Games folder" or "Emulators folder" or "Roms folder" or "Cart identity" or "CLC runtime");
        var ready = essential.All(item => item.Passed) && approvals.Count > 0 && identity is not null;
        return new(ready, identity is not null, identity, approvals, checks);
    }

    public async Task<PhysicalCartReadinessReport> PrepareAsync(
        string mediaRoot,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        Directory.CreateDirectory(root);
        foreach (var folder in new[] { "Cart", "Games" }) Directory.CreateDirectory(Path.Combine(root, folder));
        EmulatorPortableLayout.Create(root);
        var identityPath = CartIdentityService.GetIdentityPath(root);
        if (!File.Exists(identityPath))
        {
            var identities = new CartIdentityService();
            await identities.SaveNewAsync(root, identities.Create(displayName), cancellationToken);
        }
        return await InspectAsync(root, cancellationToken);
    }
}
