namespace CartLaunchCompanion.Core.Launching;

public interface IGameLaunchSession : IAsyncDisposable
{
    bool CanMonitor { get; }
    bool WasGameObserved { get; }

    Task WaitForExitAsync(
        CancellationToken cancellationToken = default);
}
