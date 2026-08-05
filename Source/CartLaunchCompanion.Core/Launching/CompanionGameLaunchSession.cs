using System.Diagnostics;

namespace CartLaunchCompanion.Core.Launching;

public sealed class CompanionGameLaunchSession(
    IGameLaunchSession inner,
    Process companionProcess,
    bool closeAfterGame) : IGameLaunchSession
{
    public bool CanMonitor => inner.CanMonitor;

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        try { await inner.WaitForExitAsync(cancellationToken); }
        finally { CloseCompanion(); }
    }

    public async ValueTask DisposeAsync()
    {
        CloseCompanion();
        await inner.DisposeAsync();
        companionProcess.Dispose();
    }

    private void CloseCompanion()
    {
        if (!closeAfterGame) return;
        try
        {
            if (!companionProcess.HasExited) companionProcess.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}
