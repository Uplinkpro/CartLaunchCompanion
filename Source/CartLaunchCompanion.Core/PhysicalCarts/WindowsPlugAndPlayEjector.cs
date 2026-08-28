using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CartLaunchCompanion.Core.PhysicalCarts;

[SupportedOSPlatform("windows")]
internal static class WindowsPlugAndPlayEjector
{
    private static readonly Guid DiskInterfaceClass = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint OpenExisting = 3;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint IoctlStorageGetDeviceNumber = 0x002D1080;
    private const uint DnRemovable = 0x00004000;
    private const uint CrSuccess = 0;
    private const int ErrorNoMoreItems = 259;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    internal static uint GetDeviceNumber(string volumeRoot)
    {
        using var handle = CreateFile(
            $@"\\.\{volumeRoot[..2]}", 0, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException(
                $"Windows could not identify the cart's physical disk (error {Marshal.GetLastWin32Error()}).");
        return QueryDeviceNumber(handle).DeviceNumber;
    }

    internal static void RequestEject(uint deviceNumber)
    {
        var diskDevInst = FindDiskDeviceInstance(deviceNumber);
        var removableDevInst = FindHighestRemovableParent(diskDevInst);
        var vetoName = new StringBuilder(260);
        var result = CM_Request_Device_EjectW(
            removableDevInst, out var vetoType, vetoName, (uint)vetoName.Capacity, 0);
        if (result == CrSuccess) return;

        var veto = DescribeVeto(vetoType, vetoName.ToString());
        throw new IOException(
            $"Windows Plug and Play rejected safe removal ({veto}; configuration error 0x{result:X8}).");
    }

    private static uint FindDiskDeviceInstance(uint targetDeviceNumber)
    {
        var interfaceClass = DiskInterfaceClass;
        var deviceInfoSet = SetupDiGetClassDevsW(
            ref interfaceClass, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == InvalidHandleValue)
            throw new IOException(
                $"Windows could not enumerate disk devices (error {Marshal.GetLastWin32Error()}).");

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                };
                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet, IntPtr.Zero, ref interfaceClass, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems) break;
                    throw new IOException($"Windows disk enumeration failed (error {error}).");
                }

                var deviceInfo = new SpDevInfoData
                {
                    Size = (uint)Marshal.SizeOf<SpDevInfoData>()
                };
                SetupDiGetDeviceInterfaceDetailW(
                    deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, ref deviceInfo);
                var sizeError = Marshal.GetLastWin32Error();
                if (requiredSize == 0)
                    throw new IOException($"Windows could not size a disk interface path (error {sizeError}).");

                var detail = Marshal.AllocHGlobal(checked((int)requiredSize));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(
                            deviceInfoSet, ref interfaceData, detail, requiredSize,
                            out _, ref deviceInfo))
                        throw new IOException(
                            $"Windows could not read a disk interface path (error {Marshal.GetLastWin32Error()}).");

                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    using var disk = CreateFile(
                        path, 0, FileShareRead | FileShareWrite,
                        IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (disk.IsInvalid) continue;
                    if (QueryDeviceNumber(disk).DeviceNumber == targetDeviceNumber)
                        return deviceInfo.DevInst;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(deviceInfoSet); }

        throw new IOException("Windows could not map the cart volume to its Plug and Play disk device.");
    }

    private static uint FindHighestRemovableParent(uint diskDevInst)
    {
        var parentResult = CM_Get_Parent(out var current, diskDevInst, 0);
        if (parentResult != CrSuccess)
            throw new IOException(
                $"Windows could not locate the cart disk's parent device (configuration error 0x{parentResult:X8}).");

        uint? removableAncestor = null;
        for (var depth = 0; depth < 32; depth++)
        {
            var statusResult = CM_Get_DevNode_Status(out var status, out _, current, 0);
            if (statusResult != CrSuccess)
                throw new IOException(
                    $"Windows could not inspect the cart device tree (configuration error 0x{statusResult:X8}).");
            // A USB storage stack can expose more than one removable node. The
            // nearest node may still have the volume as a dependent child, so
            // eject the highest removable owner (normally the USB bridge) to
            // remove the volume and disk together.
            if ((status & DnRemovable) != 0) removableAncestor = current;

            parentResult = CM_Get_Parent(out var parent, current, 0);
            if (parentResult != CrSuccess) break;
            current = parent;
        }

        return removableAncestor ?? throw new IOException(
            "Windows found the cart's physical disk, but no safely removable Plug and Play device owns it.");
    }

    private static StorageDeviceNumber QueryDeviceNumber(SafeFileHandle handle)
    {
        if (!DeviceIoControl(
                handle, IoctlStorageGetDeviceNumber, IntPtr.Zero, 0,
                out var number, (uint)Marshal.SizeOf<StorageDeviceNumber>(), out _, IntPtr.Zero))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "Windows could not read a storage device number.");
        return number;
    }

    internal static string DescribeVeto(PnpVetoType vetoType, string vetoName)
    {
        var description = vetoType switch
        {
            PnpVetoType.Unknown => "unknown veto",
            PnpVetoType.LegacyDevice => "a legacy device is using the cart",
            PnpVetoType.PendingClose => "an application is still closing a cart file",
            PnpVetoType.WindowsApp => "a Windows application is using the cart",
            PnpVetoType.WindowsService => "a Windows service is using the cart",
            PnpVetoType.OutstandingOpen => "a file or folder on the cart is still open",
            PnpVetoType.Device => "another device depends on the cart",
            PnpVetoType.Driver => "a device driver is using the cart",
            PnpVetoType.IllegalDeviceRequest => "the device rejected the removal request",
            PnpVetoType.InsufficientPower => "Windows cannot safely change the device power state",
            PnpVetoType.NonDisableable => "the device cannot be disabled safely",
            _ => "Windows did not provide a veto reason"
        };
        return string.IsNullOrWhiteSpace(vetoName) ? description : $"{description}: {vetoName}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceNumber
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    internal enum PnpVetoType : uint
    {
        Unknown,
        LegacyDevice,
        PendingClose,
        WindowsApp,
        WindowsService,
        OutstandingOpen,
        Device,
        Driver,
        IllegalDeviceRequest,
        InsufficientPower,
        NonDisableable,
        LegacyDriver
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        out StorageDeviceNumber output, uint outputSize, out uint returned, IntPtr overlapped);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, IntPtr enumerator, IntPtr parentWindow, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint detailDataSize,
        out uint requiredSize, ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(
        out uint status, out uint problemNumber, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Request_Device_EjectW(
        uint devInst, out PnpVetoType vetoType, StringBuilder vetoName,
        uint nameLength, uint flags);
}
