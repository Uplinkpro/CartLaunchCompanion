namespace CartLaunchCompanion.Core.Launching;

public interface IGameLaunchSession : IAsyncDisposable
{
    bool CanMonitor { get; }

    Task WaitForExitAsync(
        CancellationToken cancellationToken = default);
}
