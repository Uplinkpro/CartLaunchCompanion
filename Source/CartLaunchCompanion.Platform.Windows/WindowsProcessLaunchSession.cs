using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Platform.Windows;

internal sealed class WindowsProcessLaunchSession(
    Process process)
    : IGameLaunchSession
{
    public bool CanMonitor => true;

    public Task WaitForExitAsync(
        CancellationToken cancellationToken = default)
        => process.WaitForExitAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        process.Dispose();
        return ValueTask.CompletedTask;
    }
}
