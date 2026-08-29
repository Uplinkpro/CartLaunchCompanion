using System.Text.Json;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartHostAuditLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CLC-AuditTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PerformanceEvent_AppendsWithoutChangingExistingAuditIds()
    {
        Assert.Equal(0, (int)CartHostAuditEvent.HostStarted);
        Assert.Equal(16, (int)CartHostAuditEvent.EjectFailed);
        Assert.Equal(17, (int)CartHostAuditEvent.PerformanceStage);
    }

    [Fact]
    public void Write_UsesStructuredSanitizedEntryWithoutRawCartIdentity()
    {
        const string cartId = "secret-cart-id";
        new CartHostAuditLog(_root).Write(CartHostAuditEvent.VerificationRejected, "bad\r\nforged entry / path", cartId);
        var text = File.ReadAllText(Path.Combine(_root, "host-audit.jsonl"));
        var entry = JsonSerializer.Deserialize<CartHostAuditEntry>(text.Trim());
        Assert.NotNull(entry);
        Assert.Equal(CartHostAuditEvent.VerificationRejected, entry.Event);
        Assert.Equal("bad__forged_entry___path", entry.Result);
        Assert.Equal(12, entry.CartToken!.Length);
        Assert.DoesNotContain(cartId, text);
        Assert.Single(File.ReadAllLines(Path.Combine(_root, "host-audit.jsonl")));
    }

    [Fact]
    public void Write_RotatesAtLimitAndBoundsRetention()
    {
        var log = new CartHostAuditLog(_root, maximumBytes: 1024, retainedFiles: 2);
        for (var index = 0; index < 100; index++)
            log.Write(CartHostAuditEvent.ScanCompleted, new string('x', 64));
        Assert.True(File.Exists(Path.Combine(_root, "host-audit.jsonl")));
        Assert.True(File.Exists(Path.Combine(_root, "host-audit.1.jsonl")));
        Assert.True(File.Exists(Path.Combine(_root, "host-audit.2.jsonl")));
        Assert.False(File.Exists(Path.Combine(_root, "host-audit.3.jsonl")));
        Assert.All(Directory.GetFiles(_root), file => Assert.True(new FileInfo(file).Length <= 1024));
    }

    [Fact]
    public void Write_NeverThrowsWhenLogDestinationIsUnavailable()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(file, "occupied");
        var exception = Record.Exception(() => new CartHostAuditLog(file).Write(CartHostAuditEvent.HostStarted, "ok"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("ok", "ok")]
    [InlineData("", "unspecified")]
    [InlineData("space and/slash", "space_and_slash")]
    public void Sanitizer_AllowsOnlyBoundedSafeCharacters(string input, string expected) =>
        Assert.Equal(expected, CartHostAuditLog.SanitizeResult(input));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
