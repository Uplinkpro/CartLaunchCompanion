using System.Diagnostics;
using System.Runtime.InteropServices;

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
        if (!Directory.Exists(fullRoot)) return SafeMediaEjectOutcome.AlreadyRemoved;

        VerifiedCartIdentity identity;
        try { identity = await _identities.LoadAsync(fullRoot, cancellationToken); }
        catch (DirectoryNotFoundException) { return SafeMediaEjectOutcome.AlreadyRemoved; }
        catch (FileNotFoundException) when (!Directory.Exists(fullRoot)) { return SafeMediaEjectOutcome.AlreadyRemoved; }
        if (!identity.Identity.CartId.Equals(expectedCartId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The mounted media no longer matches the active trusted cart.");

        try { await _platform.EjectVolumeAsync(fullRoot, cancellationToken); }
        catch (IOException) when (!Directory.Exists(fullRoot)) { return SafeMediaEjectOutcome.AlreadyRemoved; }
        return SafeMediaEjectOutcome.Ejected;
    }
}

internal static class PhysicalMediaEjectPlatform
{
    public static IPhysicalMediaEjectPlatform Create() => OperatingSystem.IsWindows()
        ? new WindowsPhysicalMediaEjectPlatform()
        : OperatingSystem.IsLinux()
            ? new LinuxPhysicalMediaEjectPlatform()
            : throw new PlatformNotSupportedException("Safe removal is not supported on this platform.");
}

internal sealed class WindowsPhysicalMediaEjectPlatform : IPhysicalMediaEjectPlatform
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018, FsctlDismountVolume = 0x00090020, IoctlStorageEjectMedia = 0x002D4808;

    public Task EjectVolumeAsync(string volumeRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var systemRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(Path.GetFullPath(volumeRoot)) ?? "");
        if (!volumeRoot.Equals(systemRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Safe removal requires the exact mounted-drive root.");
        if (volumeRoot.Length < 2 || volumeRoot[1] != ':') throw new InvalidDataException("Only a mounted drive can be ejected.");
        using var handle = CreateFile($@"\\.\{volumeRoot[..2]}", GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Windows could not lock the cart. Close programs using it and try again.");
        if (!FlushFileBuffers(handle)) throw new IOException("Windows could not flush pending cart writes.");
        if (!DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero) ||
            !DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero) ||
            !DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException("Windows could not safely eject the cart. Close programs using it and try again.");
        return Task.CompletedTask;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushFileBuffers(Microsoft.Win32.SafeHandles.SafeFileHandle handle);
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
