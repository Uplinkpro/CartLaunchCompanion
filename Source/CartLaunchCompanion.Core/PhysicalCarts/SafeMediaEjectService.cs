using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public enum SafeMediaEjectOutcome { Ejected, AlreadyRemoved }

public interface IPhysicalMediaEjectPlatform
{
    Task EjectVolumeAsync(string volumeRoot, CancellationToken cancellationToken);
}

public sealed class SafeMediaEjectService(
    IPhysicalMediaEjectPlatform? platform = null,
    CartIdentityService? identities = null)
{
    private readonly IPhysicalMediaEjectPlatform _platform = platform ?? PhysicalMediaEjectPlatform.Create();
    private readonly CartIdentityService _identities = identities ?? new CartIdentityService();

    public async Task<SafeMediaEjectOutcome> EjectAsync(
        string mediaRoot,
        string expectedCartId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedCartId) || expectedCartId.Length > 128)
            throw new InvalidDataException("The trusted cart identity is invalid.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        if (!IsRootPresentWithoutProbing(fullRoot)) return SafeMediaEjectOutcome.AlreadyRemoved;

        VerifiedCartIdentity identity;
        try { identity = await _identities.LoadAsync(fullRoot, cancellationToken); }
        catch (DirectoryNotFoundException) { return SafeMediaEjectOutcome.AlreadyRemoved; }
        catch (FileNotFoundException) when (!IsRootPresentWithoutProbing(fullRoot)) { return SafeMediaEjectOutcome.AlreadyRemoved; }
        if (!identity.Identity.CartId.Equals(expectedCartId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The mounted media no longer matches the active trusted cart.");

        try { await _platform.EjectVolumeAsync(fullRoot, cancellationToken); }
        // Directory.Exists on a just-dismounted fixed-media USB enclosure can
        // make Windows probe and remount it. Check the logical-drive bitmask
        // instead so a bridge that finishes removal while reporting an I/O
        // error is correctly treated as already removed.
        catch (IOException) when (!IsRootPresentWithoutProbing(fullRoot)) { return SafeMediaEjectOutcome.AlreadyRemoved; }
        return SafeMediaEjectOutcome.Ejected;
    }

    private static bool IsRootPresentWithoutProbing(string fullRoot)
    {
        if (!OperatingSystem.IsWindows()) return Directory.Exists(fullRoot);
        var pathRoot = Path.GetPathRoot(fullRoot);
        if (string.IsNullOrWhiteSpace(pathRoot) || pathRoot.Length < 2 || pathRoot[1] != ':' ||
            !Path.TrimEndingDirectorySeparator(pathRoot).Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
            return Directory.Exists(fullRoot);
        var letter = char.ToUpperInvariant(pathRoot[0]);
        return letter is >= 'A' and <= 'Z' &&
               (GetLogicalDrives() & (1u << (letter - 'A'))) != 0;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetLogicalDrives();
}

internal static class PhysicalMediaEjectPlatform
{
    public static IPhysicalMediaEjectPlatform Create() => OperatingSystem.IsWindows()
        ? new WindowsPhysicalMediaEjectPlatform()
        : OperatingSystem.IsLinux()
            ? new LinuxPhysicalMediaEjectPlatform()
            : throw new PlatformNotSupportedException("Safe removal is not supported on this platform.");
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsPhysicalMediaEjectPlatform : IPhysicalMediaEjectPlatform
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018, FsctlDismountVolume = 0x00090020, IoctlStorageEjectMedia = 0x002D4808;

    public async Task EjectVolumeAsync(string volumeRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var systemRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(Path.GetFullPath(volumeRoot)) ?? "");
        if (!volumeRoot.Equals(systemRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Safe removal requires the exact mounted-drive root.");
        if (volumeRoot.Length < 2 || volumeRoot[1] != ':') throw new InvalidDataException("Only a mounted drive can be ejected.");
        var deviceNumber = WindowsPlugAndPlayEjector.GetDeviceNumber(volumeRoot);
        Microsoft.Win32.SafeHandles.SafeFileHandle? handle = null;
        var lastError = 0;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            handle = CreateFile($@"\\.\{volumeRoot[..2]}", GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (!handle.IsInvalid) break;
            lastError = Marshal.GetLastWin32Error();
            handle.Dispose();
            handle = null;
            await Task.Delay(100, cancellationToken);
        }
        if (handle is null)
            throw new IOException($"Windows could not lock the cart after closing its Explorer tab (error {lastError}). Close other programs using it and try again.");
        using (handle)
        {
            if (!FlushFileBuffers(handle))
                throw new IOException($"Windows could not flush pending cart writes (error {Marshal.GetLastWin32Error()}).");
            if (!DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException($"Windows could not lock the cart volume (error {Marshal.GetLastWin32Error()}).");
            if (!DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException($"Windows could not dismount the cart volume (error {Marshal.GetLastWin32Error()}).");
            if (!DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException($"Windows dismounted the cart but its enclosure did not accept the eject command (error {Marshal.GetLastWin32Error()}).");
        }

        if (await WaitForRemovalAsync(systemRoot, TimeSpan.FromSeconds(1), cancellationToken)) return;

        // USB-to-SATA/NVMe bridges commonly expose their volume as fixed media.
        // IOCTL_STORAGE_EJECT_MEDIA can report success for those bridges without
        // asking Plug and Play to remove the USB device. Request removal of the
        // nearest removable PnP ancestor and preserve Windows' veto reason. Some
        // UAS bridges briefly remount before completing the accepted removal, so
        // allow Windows enough time to finish instead of issuing a competing retry.
        WindowsPlugAndPlayEjector.RequestEject(deviceNumber);
        if (await WaitForRemovalAsync(systemRoot, TimeSpan.FromSeconds(15), cancellationToken)) return;

        throw new IOException(
            "Windows Plug and Play accepted safe removal, but the cart remained mounted. " +
            "Do not unplug it; close applications using the cart and try again.");
    }

    private static async Task<bool> WaitForRemovalAsync(
        string systemRoot,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Directory.Exists opens/probes a drive path and can cause Windows
            // to remount a just-dismounted USB volume. GetLogicalDrives checks
            // the mount bit without touching the cart filesystem.
            if (!IsMountedDrive(systemRoot)) return true;
            await Task.Delay(100, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);
        return !IsMountedDrive(systemRoot);
    }

    private static bool IsMountedDrive(string systemRoot)
    {
        var letter = char.ToUpperInvariant(systemRoot[0]);
        if (letter is < 'A' or > 'Z') return false;
        return (GetLogicalDrives() & (1u << (letter - 'A'))) != 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushFileBuffers(Microsoft.Win32.SafeHandles.SafeFileHandle handle);
    [DllImport("kernel32.dll")] private static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}

internal sealed class LinuxPhysicalMediaEjectPlatform : IPhysicalMediaEjectPlatform
{
    public async Task EjectVolumeAsync(string volumeRoot, CancellationToken cancellationToken)
    {
        var executable = new[] { "/usr/bin/udisksctl", "/bin/udisksctl" }.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("udisksctl is required for safe removal on this Linux system.");
        var device = ResolveDevice(volumeRoot);
        await RunAsync(executable, ["unmount", "-b", device], cancellationToken);
        await RunAsync(executable, ["power-off", "-b", device], cancellationToken);
    }

    internal static string ResolveDevice(string root, IEnumerable<string>? mountInfo = null)
    {
        var target = Path.TrimEndingDirectorySeparator(root).Replace("\\040", " ");
        foreach (var line in mountInfo ?? File.ReadLines("/proc/self/mountinfo"))
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
}
