using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record DetectedPhysicalCart(string MediaRoot, VerifiedCartIdentity Identity);

public interface IMountRootProvider { IEnumerable<string> GetMountedRoots(); }

public sealed class SystemMountRootProvider : IMountRootProvider
{
    private const uint DriveRemovable = 2;
    private const uint DriveFixed = 3;

    public IEnumerable<string> GetMountedRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            // DriveInfo.DriveType can open/probe a volume. During safe eject,
            // that can remount a fixed-media USB enclosure after CLC has just
            // dismounted it. Read the logical-drive bitmask and drive type
            // without touching the cart filesystem instead.
            var mounted = GetLogicalDrives();
            for (var index = 0; index < 26; index++)
            {
                if ((mounted & (1u << index)) == 0) continue;
                var root = $"{(char)('A' + index)}:\\";
                var type = GetDriveTypeW(root);
                if (type is DriveRemovable or DriveFixed) yield return root;
            }
            yield break;
        }
        var user = Environment.UserName;
        foreach (var parent in new[] { Path.Combine("/run/media", user), Path.Combine("/media", user), "/run/media", "/media", "/mnt" })
        {
            if (!Directory.Exists(parent)) continue;
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(parent).Take(256).ToArray(); }
            catch { continue; }
            foreach (var child in children) yield return child;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetLogicalDrives();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string rootPathName);
}

public sealed class MountedCartDetector(IMountRootProvider mountRoots, CartIdentityService identities)
{
    private readonly ConcurrentDictionary<string, byte> _ignoredRoots = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    public async Task IgnoreUntilRemovedAsync(string mediaRoot, CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(mediaRoot);
        await _scanGate.WaitAsync(cancellationToken);
        try { _ignoredRoots[root] = 0; }
        finally { _scanGate.Release(); }
    }

    public void RestoreRoot(string mediaRoot) => _ignoredRoots.TryRemove(NormalizeRoot(mediaRoot), out _);

    public async Task<IReadOnlyList<DetectedPhysicalCart>> ScanAsync(CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            var found = new List<DetectedPhysicalCart>();
            var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var candidates = new List<string>();
            foreach (var candidate in mountRoots.GetMountedRoots().Take(512))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { candidates.Add(NormalizeRoot(candidate)); }
                catch { }
            }
            var mounted = candidates.ToHashSet(comparison);
            foreach (var ignored in _ignoredRoots.Keys.Where(root => !mounted.Contains(root)))
                _ignoredRoots.TryRemove(ignored, out _);

            var seen = new HashSet<string>(comparison);
            foreach (var root in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(root) || _ignoredRoots.ContainsKey(root) ||
                    !File.Exists(CartIdentityService.GetIdentityPath(root))) continue;
                try { found.Add(new(root, await identities.LoadAsync(root, cancellationToken))); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (InvalidDataException) { }
                catch (System.Text.Json.JsonException) { }
            }
            return found;
        }
        finally { _scanGate.Release(); }
    }

    private static string NormalizeRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

public sealed class PhysicalCartMonitor(
    MountedCartDetector detector,
    TimeSpan? interval = null,
    Action<DetectedPhysicalCart, TimeSpan>? insertionScanCompleted = null) : IAsyncDisposable
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private bool _baselineEstablished;
    public event EventHandler<DetectedPhysicalCart>? CartInserted;
    public event EventHandler<string>? CartRemoved;

    public void Start() => _loop ??= MonitorAsync(_stop.Token);
    public Task IgnoreUntilRemovedAsync(string mediaRoot, CancellationToken cancellationToken = default) =>
        detector.IgnoreUntilRemovedAsync(mediaRoot, cancellationToken);
    public void RestoreRoot(string mediaRoot) => detector.RestoreRoot(mediaRoot);
    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            var scanTimer = Stopwatch.StartNew();
            IReadOnlyList<DetectedPhysicalCart> carts;
            try { carts = await detector.ScanAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            var current = carts.Select(cart => cart.MediaRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!_baselineEstablished)
            {
                _known = current;
                _baselineEstablished = true;
                continue;
            }
            var scanElapsed = scanTimer.Elapsed;
            foreach (var cart in carts.Where(cart => !_known.Contains(cart.MediaRoot)))
            {
                try { insertionScanCompleted?.Invoke(cart, scanElapsed); }
                catch { }
                CartInserted?.Invoke(this, cart);
            }
            foreach (var removed in _known.Where(root => !current.Contains(root))) CartRemoved?.Invoke(this, removed);
            _known = current;
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
