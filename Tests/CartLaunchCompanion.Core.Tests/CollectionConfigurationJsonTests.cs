using CartLaunchCompanion.Core.Configuration;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CollectionConfigurationJsonTests
{
    [Fact]
    public async Task SaveAndLoadPreserveCollectionLogo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CLC-collection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var expected = new CollectionConfiguration
            {
                Enabled = true,
                Name = "Test Series",
                Logo = "System/Assets/Collections/TestSeries/Logo.png"
            };

            await CollectionConfigurationJson.SaveAsync(folder, expected);
            var actual = await CollectionConfigurationJson.LoadAsync(folder);

            Assert.True(actual.Enabled);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Logo, actual.Logo);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
