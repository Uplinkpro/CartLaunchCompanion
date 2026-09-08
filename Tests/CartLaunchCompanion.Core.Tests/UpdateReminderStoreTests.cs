using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.Tests;

public sealed class UpdateReminderStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-UpdateReminderTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SkipOnlySuppressesTheSelectedVersion()
    {
        var store = new UpdateReminderStore(_root);

        await store.SkipAsync("2.9.0");

        Assert.True(store.IsSkipped("2.9.0"));
        Assert.False(store.IsSkipped("2.10.0"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
