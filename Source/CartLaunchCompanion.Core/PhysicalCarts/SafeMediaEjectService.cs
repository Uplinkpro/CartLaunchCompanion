using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed class SafeMediaEjectService
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlDismountVolume = 0x00090020;
    private const uint IoctlStorageEjectMedia = 0x002D4808;

    public async Task EjectAsync(string mediaRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(mediaRoot)) ?? throw new InvalidDataException("The media root is invalid.");
        if (OperatingSystem.IsWindows()) { EjectWindows(root); return; }
        if (OperatingSystem.IsLinux()) { await EjectLinuxAsync(root, cancellationToken); return; }
        throw new PlatformNotSupportedException("Safe removal is not supported on this platform.");
    }

    private static void EjectWindows(string root)
    {
        if (root.Length < 2 || root[1] != ':') throw new InvalidDataException("Only a mounted drive can be ejected.");
        using var handle = CreateFile($@"\\.\{root[..2]}", GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows could not lock the cart. Close programs using it and try again.");
        if (!FlushFileBuffers(handle)) throw new IOException("Windows could not flush pending cart writes.");
        if (!DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero) ||
            !DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero) ||
            !DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException("Windows could not safely eject the cart. Close programs using it and try again.");
    }

    private static async Task EjectLinuxAsync(string root, CancellationToken cancellationToken)
    {
        var executable = new[] { "/usr/bin/udisksctl", "/bin/udisksctl" }.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("udisksctl is required for safe removal on this Linux system.");
        var device = ResolveLinuxDevice(root);
        await RunAsync(executable, ["unmount", "-b", device], cancellationToken);
        await RunAsync(executable, ["power-off", "-b", device], cancellationToken);
    }

    private static string ResolveLinuxDevice(string root)
    {
        var target = Path.TrimEndingDirectorySeparator(root).Replace("\\040", " ");
        foreach (var line in File.ReadLines("/proc/self/mountinfo"))
        {
            var halves = line.Split(" - ", 2, StringSplitOptions.None);
            if (halves.Length != 2) continue;
            var left = halves[0].Split(' '); var right = halves[1].Split(' ');
            if (left.Length > 4 && right.Length > 1 && left[4].Replace("\\040", " ") == target && right[1].StartsWith("/dev/", StringComparison.Ordinal)) return right[1];
        }
        throw new IOException("The cart's block device could not be identified safely.");
    }

    private static async Task RunAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The safe-removal tool did not start.");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new IOException((await error).Trim());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushFileBuffers(Microsoft.Win32.SafeHandles.SafeFileHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
