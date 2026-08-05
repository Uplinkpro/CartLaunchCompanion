namespace CartLaunchCompanion.Core.Launching;

public sealed class GameLaunchResult
{
    private GameLaunchResult(
        bool succeeded,
        string message,
        IGameLaunchSession? session)
    {
        Succeeded = succeeded;
        Message = message;
        Session = session;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public IGameLaunchSession? Session { get; }

    public static GameLaunchResult Success(
        string message,
        IGameLaunchSession? session = null)
        => new(true, message, session);

    public static GameLaunchResult Failure(string message)
        => new(false, message, null);
}
