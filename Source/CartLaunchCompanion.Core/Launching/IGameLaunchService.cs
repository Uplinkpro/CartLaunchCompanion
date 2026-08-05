namespace CartLaunchCompanion.Core.Launching;

public interface IGameLaunchService
{
    Task<GameLaunchResult> LaunchAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken = default);
}
