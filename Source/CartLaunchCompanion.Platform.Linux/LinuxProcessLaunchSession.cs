using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Platform.Linux;

internal sealed class LinuxProcessLaunchSession(
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
