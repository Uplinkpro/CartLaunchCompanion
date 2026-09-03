using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public enum CartHostAuditEvent
{
    HostStarted, ScanCompleted, CartInserted, CartRemoved, TrustGranted, TrustRevoked,
    VerificationStarted, VerificationAccepted, VerificationRejected,
    LaunchStarted, LaunchEnded, EjectRequested, EjectAccepted, EjectRejected,
    EjectCompleted, EjectAlreadyRemoved, EjectFailed,
    PerformanceStage = 17,
    SetupOffered = 18, SetupAccepted = 19, SetupDeclined = 20
}

public sealed record CartHostAuditEntry(
    DateTimeOffset TimestampUtc,
    CartHostAuditEvent Event,
    string Result,
    string? CartToken);

public sealed class CartHostAuditLog
{
    public const long DefaultMaximumBytes = 512 * 1024;
    public const int DefaultRetainedFiles = 3;
    private readonly string _directory;
    private readonly long _maximumBytes;
    private readonly int _retainedFiles;
    private readonly object _sync = new();

    public CartHostAuditLog(string directory, long maximumBytes = DefaultMaximumBytes, int retainedFiles = DefaultRetainedFiles)
    {
        _directory = Path.GetFullPath(directory);
        if (maximumBytes is < 1024 or > 10 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (retainedFiles is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        _maximumBytes = maximumBytes;
        _retainedFiles = retainedFiles;
    }

    public void Write(CartHostAuditEvent auditEvent, string result, string? cartId = null)
    {
        try
        {
            var entry = new CartHostAuditEntry(DateTimeOffset.UtcNow, auditEvent, SanitizeResult(result), Tokenize(cartId));
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            var bytes = Encoding.UTF8.GetByteCount(line);
            lock (_sync)
            {
                Directory.CreateDirectory(_directory);
                var current = Path.Combine(_directory, "host-audit.jsonl");
                if (File.Exists(current) && new FileInfo(current).Length + bytes > _maximumBytes) Rotate(current);
                File.AppendAllText(current, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never affect cart verification, launch, or safe removal.
        }
    }

    private void Rotate(string current)
    {
        for (var index = _retainedFiles; index >= 1; index--)
        {
            var source = index == 1 ? current : Path.Combine(_directory, $"host-audit.{index - 1}.jsonl");
            var destination = Path.Combine(_directory, $"host-audit.{index}.jsonl");
            if (!File.Exists(source)) continue;
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(source, destination);
        }
    }

    internal static string SanitizeResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return "unspecified";
        var builder = new StringBuilder(Math.Min(result.Length, 64));
        foreach (var character in result.Take(64))
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        return builder.ToString();
    }

    internal static string? Tokenize(string? cartId)
    {
        if (string.IsNullOrWhiteSpace(cartId)) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cartId))).ToLowerInvariant()[..12];
    }
}
