using System.Text;
using System.Text.Json;
using CartLaunchCompanion.Core.PhysicalCarts;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.Tests;

public sealed class PhysicalCartFuzzTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-FuzzTests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [MemberData(nameof(MalformedIdentityJson))]
    public async Task IdentityManifest_RejectsMalformedBoundaries(string json)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, CartIdentityService.FileName), json);
        await Assert.ThrowsAnyAsync<Exception>(() => new CartIdentityService().LoadAsync(_root));
    }

    public static TheoryData<string> MalformedIdentityJson() => new()
    {
        "{}",
        "{\"FormatVersion\":1,\"FormatVersion\":1}",
        "{\"FormatVersion\":1,\"SecurityVersion\":1,\"CartId\":\"11111111-1111-1111-1111-111111111111\",\"DisplayName\":\"bad\\u0000name\",\"CreatedUtc\":\"2026-01-01T00:00:00Z\"}",
        "{\"FormatVersion\":1,\"SecurityVersion\":1,\"CartId\":\"11111111-1111-1111-1111-111111111111\",\"DisplayName\":\"\\uD800\",\"CreatedUtc\":\"2026-01-01T00:00:00Z\"}",
        new string('[', 20) + new string(']', 20),
        "not-json"
    };

    [Fact]
    public async Task IdentityManifest_DeterministicRandomMalformedInputAlwaysFailsClosed()
    {
        Directory.CreateDirectory(_root);
        var random = new Random(0x434C43);
        for (var index = 0; index < 250; index++)
        {
            var bytes = new byte[random.Next(1, 512)];
            random.NextBytes(bytes);
            await File.WriteAllBytesAsync(Path.Combine(_root, CartIdentityService.FileName), bytes);
            await Assert.ThrowsAnyAsync<Exception>(() => new CartIdentityService().LoadAsync(_root));
        }
        Assert.Single(Directory.EnumerateFiles(_root));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("folder//file.dll")]
    [InlineData("folder/./file.dll")]
    [InlineData("folder/../file.dll")]
    [InlineData("folder\\\\file.dll")]
    [InlineData("folder/file.dll\nforged")]
    [InlineData("C:/Windows/file.dll")]
    [InlineData("//server/share.dll")]
    public void RuntimePathPolicy_RejectsNonCanonicalOrEscapingPaths(string path)
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<InvalidDataException>(() => RuntimePathPolicy.ResolveContainedFile(_root, path));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escape.dll")));
    }

    [Fact]
    public async Task TrustedInventory_RejectsUnsafePathsBeforeStaging()
    {
        Directory.CreateDirectory(_root);
        var storePath = Path.Combine(_root, "trusted-carts.json");
        var file = new RuntimeUpdateFile { Path = "../outside.dll", Length = 1, Sha256 = new string('a', 64) };
        var approval = new TrustedRuntimeApproval
        {
            Platform = "Windows-x64", EntryPoint = "CartLaunchCompanion.Desktop.exe", Files = [file],
            RootFingerprint = RuntimeIntegrityVerifier.ComputeRootFingerprint([file])
        };
        var database = new TrustedCartDatabase
        {
            Carts = [new TrustedCartRecord
            {
                CartId = Guid.NewGuid().ToString("D"), DisplayName = "Unsafe", IdentityFingerprint = new string('b', 64),
                TrustedUtc = DateTimeOffset.UtcNow, RuntimeApprovals = [approval]
            }]
        };
        await File.WriteAllTextAsync(storePath, JsonSerializer.Serialize(database));
        await Assert.ThrowsAsync<InvalidDataException>(() => new TrustedCartStore(storePath).LoadAsync());
    }

    [Fact]
    public async Task RuntimeApproval_RejectsMoreThanMaximumFiles()
    {
        var runtime = Path.Combine(_root, "Cart", "System", "Windows-x64");
        Directory.CreateDirectory(runtime);
        for (var index = 0; index <= RuntimeIntegrityVerifier.MaximumFiles; index++)
            File.WriteAllText(Path.Combine(runtime, $"f{index:D4}.dll"), "x");
        await Assert.ThrowsAsync<InvalidDataException>(() => new TrustedRuntimeStagingService().CreateApprovalsAsync(_root));
    }

    [Fact]
    public void RuntimeFingerprint_IsOrderIndependentButPathSensitive()
    {
        var a = new RuntimeUpdateFile { Path = "a.dll", Length = 1, Sha256 = new string('a', 64) };
        var b = new RuntimeUpdateFile { Path = "b.dll", Length = 2, Sha256 = new string('b', 64) };
        Assert.Equal(RuntimeIntegrityVerifier.ComputeRootFingerprint([a, b]), RuntimeIntegrityVerifier.ComputeRootFingerprint([b, a]));
        var changed = new RuntimeUpdateFile { Path = "folder/a.dll", Length = 1, Sha256 = new string('a', 64) };
        Assert.NotEqual(RuntimeIntegrityVerifier.ComputeRootFingerprint([a]), RuntimeIntegrityVerifier.ComputeRootFingerprint([changed]));
    }

    [Fact]
    public void PathPolicy_RejectsDirectoryLinkWhenSupported()
    {
        var outside = Path.Combine(_root, "outside");
        var payload = Path.Combine(_root, "payload");
        Directory.CreateDirectory(outside); Directory.CreateDirectory(payload);
        var link = Path.Combine(payload, "linked");
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        Assert.Throws<InvalidDataException>(() => RuntimePathPolicy.ResolveContainedFile(payload, "linked/file.dll"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
