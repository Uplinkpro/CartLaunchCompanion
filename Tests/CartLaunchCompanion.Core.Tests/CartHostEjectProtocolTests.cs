using System.IO.Pipes;
using System.Text;
using CartLaunchCompanion.Core.PhysicalCarts;

namespace CartLaunchCompanion.Core.Tests;

public sealed class CartHostEjectProtocolTests
{
    [Fact]
    public async Task ClientAndServer_RoundTripOnlyBoundedEjectRequest()
    {
        var pipeName = UniquePipeName();
        CartHostEjectRequest? received = null;
        await using var server = new CartHostEjectServer(request =>
        {
            received = request;
            return Task.FromResult(new CartHostEjectResponse(true, "accepted"));
        }, pipeName);
        server.Start();

        var response = await CartHostEjectProtocol.RequestAsync("trusted-cart", pipeName);

        Assert.True(response.Accepted);
        Assert.Equal(new CartHostEjectRequest(1, "eject", "trusted-cart"), received);
    }

    [Theory]
    [InlineData(0, "eject", "cart")]
    [InlineData(2, "eject", "cart")]
    [InlineData(1, "execute", "cart")]
    [InlineData(1, "eject", "")]
    public async Task Server_RejectsAnythingOutsideExactOperation(int version, string operation, string cartId)
    {
        var pipeName = UniquePipeName();
        var handlerCalls = 0;
        await using var server = new CartHostEjectServer(_ =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Task.FromResult(new CartHostEjectResponse(true, "unexpected"));
        }, pipeName);
        server.Start();

        await using var pipe = await ConnectAsync(pipeName);
        await CartHostEjectProtocol.WriteAsync(pipe, new CartHostEjectRequest(version, operation, cartId), default);
        var response = await CartHostEjectProtocol.ReadAsync<CartHostEjectResponse>(pipe, default);

        Assert.False(response.Accepted);
        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public async Task Reader_RejectsOversizedLengthBeforeAllocatingPayload()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(CartHostEjectProtocol.MaximumMessageBytes + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CartHostEjectProtocol.ReadAsync<CartHostEjectRequest>(stream, default));
    }

    [Fact]
    public async Task Reader_RejectsTruncatedPayload()
    {
        using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(100));
        await stream.WriteAsync(Encoding.UTF8.GetBytes("{}"));
        stream.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            CartHostEjectProtocol.ReadAsync<CartHostEjectRequest>(stream, default));
    }

    [Fact]
    public async Task Reader_RejectsUnknownJsonFields()
    {
        var json = Encoding.UTF8.GetBytes("""{"Version":1,"Operation":"eject","CartId":"cart","Command":"format"}""");
        using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(json.Length));
        await stream.WriteAsync(json);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CartHostEjectProtocol.ReadAsync<CartHostEjectRequest>(stream, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    public void Server_RejectsUnsafePipeNames(string pipeName) =>
        Assert.Throws<ArgumentException>(() => new CartHostEjectServer(_ =>
            Task.FromResult(new CartHostEjectResponse(true, "")), pipeName));

    [Fact]
    public async Task Client_RejectsInvalidCartIdWithoutConnecting()
    {
        var response = await CartHostEjectProtocol.RequestAsync(new string('x', 129), UniquePipeName());
        Assert.False(response.Accepted);
        Assert.Contains("identity", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(timeout.Token);
        return pipe;
    }

    private static string UniquePipeName() => "CLC.Tests." + Guid.NewGuid().ToString("N");
}
