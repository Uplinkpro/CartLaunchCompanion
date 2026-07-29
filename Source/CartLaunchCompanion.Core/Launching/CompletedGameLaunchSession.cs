namespace CartLaunchCompanion.Core.Launching;

public sealed class CompletedGameLaunchSession : IGameLaunchSession
{
    public static CompletedGameLaunchSession Instance { get; } = new();

    private CompletedGameLaunchSession()
    {
    }

    public bool CanMonitor => false;

    public Task WaitForExitAsync(
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
