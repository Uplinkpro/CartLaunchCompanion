using CartLaunchCompanion.Core.PhysicalCarts;
using System.Runtime.Versioning;

namespace CartLaunchCompanion.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlugAndPlayEjectorTests
{
    [Fact]
    public void DescribeVeto_ExplainsOutstandingOpenAndNamesBlocker()
    {
        var result = WindowsPlugAndPlayEjector.DescribeVeto(
            WindowsPlugAndPlayEjector.PnpVetoType.OutstandingOpen,
            "explorer.exe");

        Assert.Equal("a file or folder on the cart is still open: explorer.exe", result);
    }

    [Fact]
    public void DescribeVeto_HandlesUnnamedWindowsService()
    {
        var result = WindowsPlugAndPlayEjector.DescribeVeto(
            WindowsPlugAndPlayEjector.PnpVetoType.WindowsService,
            "");

        Assert.Equal("a Windows service is using the cart", result);
    }
}
