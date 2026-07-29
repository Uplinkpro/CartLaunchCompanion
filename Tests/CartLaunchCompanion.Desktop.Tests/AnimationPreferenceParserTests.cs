using CartLaunchCompanion.Desktop.Controls;

namespace CartLaunchCompanion.Desktop.Tests;

public sealed class AnimationPreferenceParserTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    public void IsReducedMotionValue_AcceptsEnabledValues(string value)
    {
        Assert.True(
            AnimationPreferenceParser.IsReducedMotionValue(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("normal")]
    public void IsReducedMotionValue_RejectsOtherValues(string? value)
    {
        Assert.False(
            AnimationPreferenceParser.IsReducedMotionValue(value));
    }
}
