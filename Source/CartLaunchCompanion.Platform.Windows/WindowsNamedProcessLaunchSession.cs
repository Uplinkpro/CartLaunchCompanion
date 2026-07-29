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

        Process[] processes = [];

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processes = Process.GetProcessesByName(normalizedName);

            if (processes.Length > 0)
                break;

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, pollSeconds)),
                cancellationToken);
        }

        if (processes.Length == 0)
            return;

        try
        {
            await Task.WhenAll(
                processes.Select(
                    process => process.WaitForExitAsync(
                        cancellationToken)));
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
