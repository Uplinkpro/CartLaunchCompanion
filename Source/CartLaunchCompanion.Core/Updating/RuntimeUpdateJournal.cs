namespace CartLaunchCompanion.Core.Updating;

public sealed class RuntimeUpdateJournal
{
    public int FormatVersion { get; set; } = 1;
    public string Platform { get; set; } = "";
    public string PreviousVersion { get; set; } = "";
    public string NewVersion { get; set; } = "";
    public RuntimeUpdateState State { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum RuntimeUpdateState
{
    Prepared,
    ActiveMovedToBackup,
    NewRuntimeActivated,
    Restarted
}
