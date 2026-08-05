namespace CartLaunchCompanion.Core.Portable;

public interface IPortablePathService
{
    PortablePaths Discover(string applicationBaseDirectory);
}
