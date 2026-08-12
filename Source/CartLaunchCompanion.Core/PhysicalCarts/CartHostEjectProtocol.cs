using System.IO.Pipes;
using System.Text.Json;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record CartHostEjectRequest(int Version, string Operation, string CartId);
public sealed record CartHostEjectResponse(bool Accepted, string Message);

public static class CartHostEjectProtocol
{
    public const string PipeName = "CartLaunchCompanion.Host.Eject.v1";
    public const int MaximumMessageBytes = 4096;

    public static async Task<CartHostEjectResponse> RequestAsync(string cartId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cartId) || cartId.Length > 128)
            return new(false, "The trusted cart identity is invalid.");
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(timeout.Token);
        await WriteAsync(pipe, new CartHostEjectRequest(1, "eject", cartId), timeout.Token);
        return await ReadAsync<CartHostEjectResponse>(pipe, timeout.Token);
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaximumMessageBytes) throw new InvalidDataException("The Host message is too large.");
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = BitConverter.ToInt32(lengthBytes);
        if (length <= 0 || length > MaximumMessageBytes) throw new InvalidDataException("The Host message length is invalid.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidDataException("The Host message is invalid.");
    }
}

public sealed class CartHostEjectServer(Func<CartHostEjectRequest, Task<CartHostEjectResponse>> handler) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public void Start() => _loop ??= RunAsync(_stop.Token);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(CartHostEjectProtocol.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                var request = await CartHostEjectProtocol.ReadAsync<CartHostEjectRequest>(pipe, cancellationToken);
                var valid = request is { Version: 1, Operation: "eject" } && !string.IsNullOrWhiteSpace(request.CartId) && request.CartId.Length <= 128;
                var response = valid ? await handler(request) : new CartHostEjectResponse(false, "The eject request was rejected.");
                await CartHostEjectProtocol.WriteAsync(pipe, response, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
