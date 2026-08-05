using System.Runtime.InteropServices;

namespace CartLaunchCompanion.Desktop.Input;

internal static class Sdl3Native
{
    internal const uint InitGamepad = 0x00002000;

    private const string LibraryName = "SDL3";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetError();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetGamepads(out int count);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_OpenGamepad(uint instanceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_CloseGamepad(IntPtr gamepad);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_GamepadConnected(IntPtr gamepad);


    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_PumpEvents();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_UpdateGamepads();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SDL_GetGamepadButton(
        IntPtr gamepad,
        SdlGamepadButton button);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern short SDL_GetGamepadAxis(
        IntPtr gamepad,
        SdlGamepadAxis axis);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SDL_GetGamepadName(IntPtr gamepad);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SDL_free(IntPtr memory);

    internal static string GetError()
    {
        var pointer = SDL_GetError();
        return pointer == IntPtr.Zero
            ? "Unknown SDL error."
            : Marshal.PtrToStringUTF8(pointer) ?? "Unknown SDL error.";
    }

    internal static string GetGamepadName(IntPtr gamepad)
    {
        var pointer = SDL_GetGamepadName(gamepad);
        return pointer == IntPtr.Zero
            ? "Gamepad"
            : Marshal.PtrToStringUTF8(pointer) ?? "Gamepad";
    }
}

internal enum SdlGamepadButton
{
    Invalid = -1,
    South = 0,
    East = 1,
    West = 2,
    North = 3,
    Back = 4,
    Guide = 5,
    Start = 6,
    LeftStick = 7,
    RightStick = 8,
    LeftShoulder = 9,
    RightShoulder = 10,
    DpadUp = 11,
    DpadDown = 12,
    DpadLeft = 13,
    DpadRight = 14
}

internal enum SdlGamepadAxis
{
    Invalid = -1,
    LeftX = 0,
    LeftY = 1,
    RightX = 2,
    RightY = 3,
    LeftTrigger = 4,
    RightTrigger = 5
}
