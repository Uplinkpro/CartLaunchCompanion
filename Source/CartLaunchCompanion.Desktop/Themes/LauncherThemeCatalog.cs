using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Desktop.Themes;

public static class LauncherThemeCatalog
{
    private static readonly LauncherTheme DefaultTheme = new(
        "#9D56E8",
        "#C08AFF",
        "#56347A",
        "#709D56E8",
        "#009D56E8",
        "#309D56E8",
        "#F4F1F8");

    public static LauncherTheme Get(LauncherKind launcher) =>
        launcher switch
        {
            LauncherKind.Xbox => new(
                "#35A936",
                "#64D765",
                "#245F28",
                "#7035A936",
                "#0035A936",
                "#3035A936",
                "#F3FFF3"),

            LauncherKind.Steam => new(
                "#3E8BFF",
                "#75B1FF",
                "#285487",
                "#703E8BFF",
                "#003E8BFF",
                "#303E8BFF",
                "#F2F7FF"),

            LauncherKind.Epic => new(
                "#A9ABB2",
                "#E2E3E7",
                "#55575D",
                "#58C7C9CF",
                "#00C7C9CF",
                "#24C7C9CF",
                "#FFFFFF"),

            LauncherKind.Heroic => new(
                "#E89A2D",
                "#FFC66E",
                "#78501E",
                "#70E89A2D",
                "#00E89A2D",
                "#30E89A2D",
                "#FFF8ED"),

            LauncherKind.GOG => new(
                "#A94FDC",
                "#D08AFF",
                "#653185",
                "#70A94FDC",
                "#00A94FDC",
                "#30A94FDC",
                "#FCF4FF"),

            LauncherKind.Ubisoft => new(
                "#24B8E0",
                "#67DBF8",
                "#24687A",
                "#7024B8E0",
                "#0024B8E0",
                "#3024B8E0",
                "#F1FCFF"),

            LauncherKind.Rockstar => new(
                "#E0A623",
                "#FFD466",
                "#745715",
                "#70E0A623",
                "#00E0A623",
                "#30E0A623",
                "#FFF9E9"),

            LauncherKind.Amazon => new(
                "#FF9900",
                "#FFC15A",
                "#80510C",
                "#70FF9900",
                "#00FF9900",
                "#30FF9900",
                "#FFF8EC"),

            LauncherKind.Flatpak => new(
                "#4A90D9",
                "#7DB8F2",
                "#315C89",
                "#704A90D9",
                "#004A90D9",
                "#304A90D9",
                "#F2F8FF"),

            LauncherKind.Wine => new(
                "#8A64D6",
                "#BA9AF5",
                "#543E7F",
                "#708A64D6",
                "#008A64D6",
                "#308A64D6",
                "#F8F3FF"),

            LauncherKind.Proton => new(
                "#6A8FFF",
                "#9DB6FF",
                "#435B94",
                "#706A8FFF",
                "#006A8FFF",
                "#306A8FFF",
                "#F5F7FF"),

            _ => DefaultTheme
        };
}
