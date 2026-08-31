using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class WindowsDriveBrandingServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-DriveBranding-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Apply_WritesIconAndLabelWithoutExecutableAutorunCommands()
    {
        var icon = Path.Combine(_root, "Cart", "System", "Assets", "AppIcon.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.WriteAllText(icon, "icon");

        var result = new WindowsDriveBrandingService().Apply(_root, "GTA Collection");
        var content = File.ReadAllText(Path.Combine(_root, WindowsDriveBrandingService.AutorunFileName));

        Assert.True(result.Applied);
        Assert.Contains(@"Icon=Cart\System\Assets\AppIcon.ico", content);
        Assert.Contains("Label=GTA Collection", content);
        Assert.DoesNotContain("Open=", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShellExecute=", content, StringComparison.OrdinalIgnoreCase);
        Assert.True(new WindowsDriveBrandingService().Inspect(_root).Applied);
    }

    [Fact]
    public void Apply_DoesNotCreateAutorunWhenIconIsMissing()
    {
        Directory.CreateDirectory(_root);
        var result = new WindowsDriveBrandingService().Apply(_root, "No Icon");
        Assert.False(result.Applied);
        Assert.False(File.Exists(Path.Combine(_root, WindowsDriveBrandingService.AutorunFileName)));
    }

    public void Dispose()
    {
        var autorun = Path.Combine(_root, WindowsDriveBrandingService.AutorunFileName);
        if (File.Exists(autorun)) File.SetAttributes(autorun, FileAttributes.Normal);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
