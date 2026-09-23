namespace CartLaunchCompanion.Core.Emulators;

public enum ControllerProtocolKind
{
    Standard, Xbox360, XboxOne, PlayStation3, PlayStation4, PlayStation5, NintendoSwitch, Steam
}

public enum ControllerFamilyKind
{
    EightBitDo,
    PlayStation3,
    PlayStation4,
    PlayStation5,
    DualSenseEdge,
    SteamController,
    SteamController2,
    Xbox360,
    XboxOne,
    XboxSeries,
    XboxElite,
    XboxAdaptive,
    Nintendo,
    GenericXboxStyle
}

public enum ControllerMappingKind { SdlStandard, BuiltInXboxFallback }

/// <summary>Stable family recognition layered on SDL's normalized gamepad protocol.</summary>
public static class ControllerCompatibilityCatalog
{
    public static ControllerFamilyKind Classify(ushort vendor, ushort product, string name,
        ControllerProtocolKind protocol)
    {
        if (vendor == 0x2dc8 || name.Contains("8BitDo", StringComparison.OrdinalIgnoreCase))
            return ControllerFamilyKind.EightBitDo;
        if (vendor == 0x28de || name.Contains("Steam Controller", StringComparison.OrdinalIgnoreCase))
            return IsSteamController2(product, name) ? ControllerFamilyKind.SteamController2 : ControllerFamilyKind.SteamController;
        if (vendor == 0x054c || protocol is ControllerProtocolKind.PlayStation3 or ControllerProtocolKind.PlayStation4 or ControllerProtocolKind.PlayStation5)
        {
            if (product == 0x0df2 || name.Contains("Edge", StringComparison.OrdinalIgnoreCase)) return ControllerFamilyKind.DualSenseEdge;
            if (product == 0x0268) return ControllerFamilyKind.PlayStation3;
            if (product is 0x05c4 or 0x09cc or 0x0ba0) return ControllerFamilyKind.PlayStation4;
            if (product == 0x0ce6) return ControllerFamilyKind.PlayStation5;
            return protocol switch
            {
                ControllerProtocolKind.PlayStation3 => ControllerFamilyKind.PlayStation3,
                ControllerProtocolKind.PlayStation4 => ControllerFamilyKind.PlayStation4,
                _ => ControllerFamilyKind.PlayStation5
            };
        }
        if (vendor == 0x045e)
        {
            if (product is 0x02e3 or 0x0b00 or 0x0b05 or 0x0b22) return ControllerFamilyKind.XboxElite;
            if (product is 0x0b0a or 0x0b0c) return ControllerFamilyKind.XboxAdaptive;
            if (product is 0x0b12 or 0x0b13) return ControllerFamilyKind.XboxSeries;
            return protocol == ControllerProtocolKind.Xbox360 ? ControllerFamilyKind.Xbox360 : ControllerFamilyKind.XboxOne;
        }
        if (protocol == ControllerProtocolKind.Xbox360) return ControllerFamilyKind.Xbox360;
        if (protocol == ControllerProtocolKind.XboxOne) return ControllerFamilyKind.XboxOne;
        if (protocol == ControllerProtocolKind.NintendoSwitch) return ControllerFamilyKind.Nintendo;
        return ControllerFamilyKind.GenericXboxStyle;
    }

    public static string DisplayName(this ControllerFamilyKind family) => family switch
    {
        ControllerFamilyKind.EightBitDo => "8BitDo",
        ControllerFamilyKind.PlayStation3 => "PlayStation 3",
        ControllerFamilyKind.PlayStation4 => "PlayStation 4",
        ControllerFamilyKind.PlayStation5 => "PlayStation 5",
        ControllerFamilyKind.DualSenseEdge => "DualSense Edge",
        ControllerFamilyKind.SteamController => "Steam Controller",
        ControllerFamilyKind.SteamController2 => "Steam Controller 2",
        ControllerFamilyKind.Xbox360 => "Xbox 360",
        ControllerFamilyKind.XboxOne => "Xbox One",
        ControllerFamilyKind.XboxSeries => "Xbox Series",
        ControllerFamilyKind.XboxElite => "Xbox Elite",
        ControllerFamilyKind.XboxAdaptive => "Xbox Adaptive",
        ControllerFamilyKind.Nintendo => "Nintendo",
        _ => "Generic Xbox-style"
    };

    private static bool IsSteamController2(ushort product, string name) =>
        product is 0x1201 or 0x1202 or 0x1302 or 0x1303 or 0x1304 or 0x1305 ||
        name.Contains("2026", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Triton", StringComparison.OrdinalIgnoreCase);
}
