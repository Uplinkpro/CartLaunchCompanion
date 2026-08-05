namespace CartLaunchCompanion.Desktop.Controls;

public static class AnimationPreferenceParser
{
    public static bool IsReducedMotionValue(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
}
