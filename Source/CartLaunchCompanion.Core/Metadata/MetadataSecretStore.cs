using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CartLaunchCompanion.Core.Metadata;

public static class MetadataSecretStore
{
    public const string SteamWebApiKey = "SteamWebApiKey";
    public const string SteamGridDbApiKey = "SteamGridDbApiKey";
    private const string ApplicationName = "CartLaunchCompanion";

    public static Task<string> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        OperatingSystem.IsWindows()
            ? Task.FromResult(ReadWindows(key))
            : OperatingSystem.IsLinux()
                ? ReadLinuxAsync(key, cancellationToken)
                : Task.FromResult("");

    public static Task WriteAsync(string key, string value, CancellationToken cancellationToken = default) =>
        OperatingSystem.IsWindows()
            ? Task.Run(() => WriteWindows(key, value), cancellationToken)
            : OperatingSystem.IsLinux()
                ? WriteLinuxAsync(key, value, cancellationToken)
                : throw new PlatformNotSupportedException("Secure API-key storage is unavailable on this operating system.");

    private static string Target(string key) => ApplicationName + "/" + key;

    private static string ReadWindows(string key)
    {
        if (!CredRead(Target(key), 1, 0, out var pointer)) return "";
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? ""
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? "";
        }
        finally { CredFree(pointer); }
    }

    private static void WriteWindows(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            CredDelete(Target(key), 1, 0);
            return;
        }
        var bytes = System.Text.Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = 1,
                TargetName = Target(key),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = 2,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static async Task<string> ReadLinuxAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var process = StartSecretTool(["lookup", "application", ApplicationName, "key", key]);
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output.TrimEnd('\r', '\n') : "";
        }
        catch (Win32Exception) { return ""; }
    }

    private static async Task WriteLinuxAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            try
            {
                using var clear = StartSecretTool(["clear", "application", ApplicationName, "key", key]);
                await clear.WaitForExitAsync(cancellationToken);
            }
            catch (Win32Exception) { }
            return;
        }
        Process process;
        try
        {
            process = StartSecretTool([
                "store", "--label=Cart Launch Companion API key",
                "application", ApplicationName, "key", key]);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "Secure key storage is unavailable. Install secret-tool and enable a desktop keyring before saving API keys.", ex);
        }

        using (process)
        {
            await process.StandardInput.WriteAsync(value.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("The Linux keyring could not save the API key. " + error.Trim());
        }
    }

    private static Process StartSecretTool(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "secret-tool",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the system keyring helper.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
