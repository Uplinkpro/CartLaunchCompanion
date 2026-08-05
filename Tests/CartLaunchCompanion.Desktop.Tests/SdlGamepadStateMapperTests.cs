using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Desktop.Input;

namespace CartLaunchCompanion.Desktop.Tests;

public sealed class SdlGamepadStateMapperTests
{
    [Fact]
    public void Map_UsesSouthEastWestForConfirmBackTrailer()
    {
        var mapper = new SdlGamepadStateMapper();
        var now = DateTimeOffset.UtcNow;

        var confirm = mapper.Map(
            true, false, false,
            false, false, false, false,
            0, 0, now);

        mapper.Reset();

        var back = mapper.Map(
            false, true, false,
            false, false, false, false,
            0, 0, now);

        mapper.Reset();

        var trailer = mapper.Map(
            false, false, true,
            false, false, false, false,
            0, 0, now);

        Assert.Contains(LauncherAction.Confirm, confirm);
        Assert.Contains(LauncherAction.Back, back);
        Assert.Contains(LauncherAction.Trailer, trailer);
    }

    [Fact]
    public void Map_SupportsDpadAndAnalogStick()
    {
        var mapper = new SdlGamepadStateMapper();
        var now = DateTimeOffset.UtcNow;

        var dpad = mapper.Map(
            false, false, false,
            false, false, true, false,
            0, 0, now);

        mapper.Reset();

        var stick = mapper.Map(
            false, false, false,
            false, false, false, false,
            22000, 0, now);

        Assert.Contains(LauncherAction.NavigateLeft, dpad);
        Assert.Contains(LauncherAction.NavigateRight, stick);
    }

    [Fact]
    public void Map_DoesNotRepeatFaceButtonWhileHeld()
    {
        var mapper = new SdlGamepadStateMapper();
        var now = DateTimeOffset.UtcNow;

        var first = mapper.Map(
            true, false, false,
            false, false, false, false,
            0, 0, now);

        var held = mapper.Map(
            true, false, false,
            false, false, false, false,
            0, 0, now.AddMilliseconds(500));

        Assert.Contains(LauncherAction.Confirm, first);
        Assert.DoesNotContain(LauncherAction.Confirm, held);
    }

    [Fact]
    public void Map_RepeatsHeldDirectionAfterInitialDelay()
    {
        var mapper = new SdlGamepadStateMapper();
        var now = DateTimeOffset.UtcNow;

        var first = mapper.Map(
            false, false, false,
            false, false, false, true,
            0, 0, now);

        var early = mapper.Map(
            false, false, false,
            false, false, false, true,
            0, 0, now.AddMilliseconds(100));

        var repeated = mapper.Map(
            false, false, false,
            false, false, false, true,
            0, 0, now.AddMilliseconds(380));

        Assert.Contains(LauncherAction.NavigateRight, first);
        Assert.DoesNotContain(LauncherAction.NavigateRight, early);
        Assert.Contains(LauncherAction.NavigateRight, repeated);
    }
}
