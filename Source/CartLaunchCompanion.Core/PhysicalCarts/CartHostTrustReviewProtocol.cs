using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record CartHostTrustReviewRequest(int Version, string Operation, string MediaRoot);
public sealed record CartHostTrustReviewResponse(bool Accepted, string Message);

public static class CartHostTrustReviewProtocol
{
    public const string PipeName = "CartLaunchCompanion.Host.TrustReview.v1";
    public const int MaximumMessageBytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4 };

    public static async Task<CartHostTrustReviewResponse> RequestAsync(string mediaRoot, string? pipeName = null, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(mediaRoot);
        if (root.Length > 1024) return new(false, "The media root is too long.");
        await using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(timeout.Token);
        await WriteAsync(pipe, new CartHostTrustReviewRequest(1, "review-trust", root), timeout.Token);
        return await ReadAsync<CartHostTrustReviewResponse>(pipe, timeout.Token);
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length > MaximumMessageBytes) throw new InvalidDataException("The Host review message is too large.");
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4]; await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = BitConverter.ToInt32(lengthBytes);
        if (length <= 0 || length > MaximumMessageBytes) throw new InvalidDataException("The Host review message length is invalid.");
        var payload = new byte[length]; await stream.ReadExactlyAsync(payload, cancellationToken);
        try { return JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? throw new InvalidDataException("The Host review message is invalid."); }
        catch (JsonException ex) { throw new InvalidDataException("The Host review message is invalid.", ex); }
    }
}

public sealed class CartHostTrustReviewServer(
    Func<CartHostTrustReviewRequest, Task<CartHostTrustReviewResponse>> handler,
    string? pipeName = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _pipeName = pipeName ?? CartHostTrustReviewProtocol.PipeName;
    private Task? _loop;
    public void Start() => _loop ??= RunAsync(_stop.Token);
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                var request = await CartHostTrustReviewProtocol.ReadAsync<CartHostTrustReviewRequest>(pipe, cancellationToken);
                var valid = request is { Version: 1, Operation: "review-trust" } && request.MediaRoot.Length is > 0 and <= 1024;
                var response = valid ? await handler(request) : new CartHostTrustReviewResponse(false, "The trust-review request was rejected.");
                await CartHostTrustReviewProtocol.WriteAsync(pipe, response, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch { }
        }
    }
    public async ValueTask DisposeAsync() { await _stop.CancelAsync(); if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { } _stop.Dispose(); }
}
