using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length != 2 || !int.TryParse(args[0], out var parentId)) return 2;
var requestedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
var local = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
var expectedRoot = Path.Combine(local, "CartLaunchCompanion", "Host", "Runtime");
if (!requestedRoot.Equals(expectedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return 3;

try
{
    using var parent = Process.GetProcessById(parentId);
    parent.WaitForExit(30_000);
}
catch (ArgumentException) { }

for (var attempt = 0; attempt < 20 && Directory.Exists(requestedRoot); attempt++)
{
    try { Directory.Delete(requestedRoot, recursive: true); }
    catch (IOException) { Thread.Sleep(250); }
    catch (UnauthorizedAccessException) { Thread.Sleep(250); }
}

if (Directory.Exists(requestedRoot)) return 4;
if (OperatingSystem.IsWindows()) NativeMethods.MoveFileEx(Environment.ProcessPath!, null, 4);
else try { File.Delete(Environment.ProcessPath!); } catch { }
return 0;

internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
