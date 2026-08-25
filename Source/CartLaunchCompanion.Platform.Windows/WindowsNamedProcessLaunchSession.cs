using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Platform.Windows;

internal sealed class WindowsNamedProcessLaunchSession(
    string processName,
    int startTimeoutSeconds,
    int pollSeconds)
    : IGameLaunchSession
{
    public bool CanMonitor => true;

    public async Task WaitForExitAsync(
        CancellationToken cancellationToken = default)
    {
        var normalizedName =
            Path.GetFileNameWithoutExtension(processName);

        var deadline =
            DateTime.UtcNow.AddSeconds(startTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsProcessRunning(normalizedName))
                break;

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, pollSeconds)),
                cancellationToken);
        }

        if (!IsProcessRunning(normalizedName))
            return;

        while (IsProcessRunning(normalizedName))
        {
            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, pollSeconds)),
                cancellationToken);
        }
    }

    private static bool IsProcessRunning(string normalizedName)
    {
        var processes = Process.GetProcessesByName(normalizedName);
        try
        {
            // Do not call WaitForExitAsync here. Some launcher-owned and elevated
            // games deny the process synchronization handle it requests even
            // though Windows still allows their presence to be enumerated.
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
