using System.Globalization;
using Avalonia.Data.Converters;
using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Configurator;

public sealed class LauncherKindSelectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LauncherKind selected || parameter is not string choices)
            return false;

        return choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(choice => Enum.TryParse<LauncherKind>(choice, true, out var candidate) && candidate == selected);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
