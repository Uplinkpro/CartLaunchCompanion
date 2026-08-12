using System.Diagnostics;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record PreparedCartLaunchSession(Process Process, PreparedCartRuntime Runtime) : IAsyncDisposable
{
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await Process.WaitForExitAsync(cancellationToken);
        return Process.ExitCode;
    }

    public async Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default)
    {
        if (Process.HasExited) return;
        try { Process.CloseMainWindow(); } catch (InvalidOperationException) { }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(gracefulTimeout);
        try { await Process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { if (!Process.HasExited) await Process.WaitForExitAsync(); }
        finally
        {
            Process.Dispose();
            TrustedRuntimeStagingService.DeleteSession(Runtime);
        }
    }
}

public sealed class PreparedCartLaunchService
{
    private static readonly string[] DangerousEnvironmentPrefixes =
    ["DOTNET_", "CORECLR_", "COMPlus_", "LD_", "DYLD_", "MONO_", "GTK_", "QT_PLUGIN_PATH", "PYTHON", "PERL", "RUBY"];

    public ProcessStartInfo CreateStartInfo(PreparedCartRuntime prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(prepared.SessionRoot));
        var executable = Path.GetFullPath(prepared.ExecutablePath);
        if (!File.Exists(executable) || !executable.StartsWith(session + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidDataException("The prepared launcher is outside its verified session.");
        var expected = prepared.Platform == "Windows-x64" ? "CartLaunchCompanion.Desktop.exe" :
            prepared.Platform == "Linux-x64" ? "CartLaunchCompanion.Desktop" : throw new InvalidDataException("The prepared platform is unsupported.");
        if (!Path.GetFileName(executable).Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("The prepared launcher entry point is invalid.");
        var cartRoot = Path.GetFullPath(prepared.CartRoot);
        if (!Directory.Exists(cartRoot) || Path.GetFileName(Path.TrimEndingDirectorySeparator(cartRoot)) != "Cart")
            throw new InvalidDataException("The prepared cart data root is invalid.");
        var cartInfo = new DirectoryInfo(cartRoot);
        if ((cartInfo.Attributes & FileAttributes.ReparsePoint) != 0 || cartInfo.LinkTarget is not null)
            throw new InvalidDataException("The prepared cart data root cannot be a link or junction.");

        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = session,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        start.ArgumentList.Add("--cart-root");
        start.ArgumentList.Add(cartRoot);
        foreach (var key in start.Environment.Keys.ToArray())
            if (DangerousEnvironmentPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                start.Environment.Remove(key);
        start.Environment["CLC_TRUSTED_CART_ID"] = prepared.CartId;
        start.Environment["CLC_RUNTIME_FINGERPRINT"] = prepared.RuntimeFingerprint;
        return start;
    }

    public PreparedCartLaunchSession Start(PreparedCartRuntime prepared)
    {
        var process = Process.Start(CreateStartInfo(prepared))
            ?? throw new InvalidOperationException("The verified Cart Launch Companion process did not start.");
        return new(process, prepared);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
