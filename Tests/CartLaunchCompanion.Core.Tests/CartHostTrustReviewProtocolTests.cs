using System.IO.Pipes;
using System.Text;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartHostTrustReviewProtocolTests
{
    [Fact]
    public async Task Review_RoundTripsOnlyMediaRootAndDoesNotGrantTrust()
    {
        var pipeName = "CLC.Review.Tests." + Guid.NewGuid().ToString("N");
        CartHostTrustReviewRequest? received = null;
        await using var server = new CartHostTrustReviewServer(request =>
        {
            received = request;
            return Task.FromResult(new CartHostTrustReviewResponse(true, "review"));
        }, pipeName);
        server.Start();
        var response = await CartHostTrustReviewProtocol.RequestAsync(Path.GetTempPath(), pipeName);
        Assert.True(response.Accepted);
        Assert.Equal("review-trust", received!.Operation);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), received.MediaRoot);
    }

    [Theory]
    [InlineData(0, "review-trust")]
    [InlineData(2, "review-trust")]
    [InlineData(1, "trust")]
    [InlineData(1, "execute")]
    public async Task Server_RejectsUnsupportedOrPrivilegeBearingOperations(int version, string operation)
    {
        var pipeName = "CLC.Review.Tests." + Guid.NewGuid().ToString("N");
        var calls = 0;
        await using var server = new CartHostTrustReviewServer(_ => { calls++; return Task.FromResult(new CartHostTrustReviewResponse(true, "bad")); }, pipeName);
        server.Start();
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await pipe.ConnectAsync(timeout.Token);
        await CartHostTrustReviewProtocol.WriteAsync(pipe, new CartHostTrustReviewRequest(version, operation, Path.GetTempPath()), timeout.Token);
        var response = await CartHostTrustReviewProtocol.ReadAsync<CartHostTrustReviewResponse>(pipe, timeout.Token);
        Assert.False(response.Accepted); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Reader_RejectsUnknownFields()
    {
        var json = Encoding.UTF8.GetBytes("""{"Version":1,"Operation":"review-trust","MediaRoot":"C:\\\\","Approve":true}""");
        using var stream = new MemoryStream(); await stream.WriteAsync(BitConverter.GetBytes(json.Length)); await stream.WriteAsync(json); stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => CartHostTrustReviewProtocol.ReadAsync<CartHostTrustReviewRequest>(stream, default));
    }
}
