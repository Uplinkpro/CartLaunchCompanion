using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public static class WindowsExplorerCartWindowService
{
    private const int ShowWindowMinimize = 6;

    public static bool TryMinimizeRootWindow(string mediaRoot)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return VisitRootWindows(mediaRoot, window =>
        {
            var handle = new IntPtr(Convert.ToInt64(Get(window, "HWND")));
            if (handle == IntPtr.Zero) return false;
            ShowWindow(handle, ShowWindowMinimize);
            return true;
        });
    }

    public static bool TryCloseRootWindow(string mediaRoot)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return VisitRootWindows(mediaRoot, window =>
        {
            Invoke(window, "Quit");
            return true;
        });
    }

    internal static bool MatchesRootLocation(string? locationUrl, string mediaRoot)
    {
        if (string.IsNullOrWhiteSpace(locationUrl) ||
            !Uri.TryCreate(locationUrl, UriKind.Absolute, out var location) || !location.IsFile)
            return false;
        try
        {
            var explorerPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.LocalPath));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
            return explorerPath.Equals(root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool VisitRootWindows(string mediaRoot, Func<object, bool> action)
    {
        object? shell = null;
        object? windows = null;
        var minimized = false;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return false;
            windows = Invoke(shell, "Windows");
            if (windows is null) return false;
            var count = Convert.ToInt32(Get(windows, "Count"));
            for (var index = 0; index < count; index++)
            {
                object? window = null;
                try
                {
                    window = Invoke(windows, "Item", index);
                    if (window is null || !MatchesRootLocation(Get(window, "LocationURL") as string, mediaRoot)) continue;
                    minimized |= action(window);
                }
                catch (COMException) { }
                catch (TargetInvocationException) { }
                finally { Release(window); }
            }
        }
        catch (COMException) { }
        catch (TargetInvocationException) { }
        finally
        {
            Release(windows);
            Release(shell);
        }
        return minimized;
    }

    private static object? Get(object target, string property) =>
        target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);

    private static object? Invoke(object target, string method, params object[] arguments) =>
        target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, arguments);

    [SupportedOSPlatform("windows")]
    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            try { Marshal.FinalReleaseComObject(value); } catch (COMException) { }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
