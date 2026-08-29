using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PhysicalCartReadinessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-Readiness-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Prepare_CreatesFoldersAndIdentityThenReportsVerifiedRuntime()
    {
        CreateRuntime();
        var report = await new PhysicalCartReadinessService().PrepareAsync(_root, "Prepared Cart");
        Assert.True(report.IsReady);
        Assert.Equal("Prepared Cart", report.Identity!.Identity.DisplayName);
        Assert.All(new[] { "Cart", "Games", "Emulators", "Roms" }, folder => Assert.True(Directory.Exists(Path.Combine(_root, folder))));
        Assert.True(Directory.Exists(Path.Combine(_root, "Emulators", "Windows", "RetroArch")));
        Assert.True(Directory.Exists(Path.Combine(_root, "Emulators", "Linux", "Dolphin")));
        Assert.True(Directory.Exists(Path.Combine(_root, "Emulators", "Shared", "Saves")));
        Assert.True(Directory.Exists(Path.Combine(_root, "Roms", "GameCube")));
        Assert.Single(report.RuntimeApprovals);
    }

    [Fact]
    public async Task Prepare_PreservesExistingValidIdentity()
    {
        CreateRuntime(); Directory.CreateDirectory(_root);
        var identities = new CartIdentityService();
        var existing = await identities.SaveNewAsync(_root, identities.Create("Original Identity"));
        var report = await new PhysicalCartReadinessService().PrepareAsync(_root, "Replacement Name");
        Assert.Equal(existing.Identity.CartId, report.Identity!.Identity.CartId);
        Assert.Equal("Original Identity", report.Identity.Identity.DisplayName);
    }

    [Fact]
    public async Task Inspect_FailsClearlyWhenRuntimeOrFoldersAreMissing()
    {
        Directory.CreateDirectory(_root);
        var report = await new PhysicalCartReadinessService().InspectAsync(_root);
        Assert.False(report.IsReady);
        Assert.Contains(report.Checks, check => check.Name == "Cart folder" && !check.Passed);
        Assert.Contains(report.Checks, check => check.Name == "Cart identity" && !check.Passed);
    }

    [Fact]
    public async Task Prepare_DoesNotReplaceInvalidExistingIdentity()
    {
        CreateRuntime(); Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, CartIdentityService.DirectoryName));
        var path = CartIdentityService.GetIdentityPath(_root);
        await File.WriteAllTextAsync(path, "invalid identity");
        var report = await new PhysicalCartReadinessService().PrepareAsync(_root, "New Cart");
        Assert.False(report.IsReady);
        Assert.Equal("invalid identity", await File.ReadAllTextAsync(path));
    }

    private void CreateRuntime()
    {
        var runtime = Path.Combine(_root, "Cart", "System", "Windows-x64");
        Directory.CreateDirectory(runtime);
        File.WriteAllText(Path.Combine(runtime, "CartLaunchCompanion.Desktop.exe"), "runtime");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
