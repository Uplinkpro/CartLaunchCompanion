namespace CartLaunchCompanion.Core.Platform;

public interface IPlatformService
{
    PlatformKind Current { get; }
}
