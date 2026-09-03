using System.Globalization;
using Avalonia.Data.Converters;
using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Configurator;

public sealed class LauncherKindDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => "None — use launch method",
            LauncherKind.Local => "EXE",
            LauncherKind.Custom => "Emulator",
            LauncherKind.BattleNet => "Battle.net",
            LauncherKind.HoYoverse => "HoYoverse",
            LauncherKind.ItchIo => "itch.io",
            LauncherKind launcher => launcher.ToString(),
            _ => ""
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
