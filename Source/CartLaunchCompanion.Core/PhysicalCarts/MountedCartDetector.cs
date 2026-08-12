namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record DetectedPhysicalCart(string MediaRoot, VerifiedCartIdentity Identity);

public interface IMountRootProvider { IEnumerable<string> GetMountedRoots(); }

public sealed class SystemMountRootProvider : IMountRootProvider
{
    public IEnumerable<string> GetMountedRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                bool ready;
                try { ready = drive.IsReady && drive.DriveType is DriveType.Removable or DriveType.Fixed; }
                catch { ready = false; }
                if (ready) yield return drive.RootDirectory.FullName;
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
}

public sealed class MountedCartDetector(IMountRootProvider mountRoots, CartIdentityService identities)
{
    public async Task<IReadOnlyList<DetectedPhysicalCart>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<DetectedPhysicalCart>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var candidate in mountRoots.GetMountedRoots().Take(512))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root;
            try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)); }
            catch { continue; }
            if (!seen.Add(root) || !File.Exists(Path.Combine(root, CartIdentityService.FileName))) continue;
            try { found.Add(new(root, await identities.LoadAsync(root, cancellationToken))); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException) { }
            catch (System.Text.Json.JsonException) { }
        }
        return found;
    }
}

public sealed class PhysicalCartMonitor(MountedCartDetector detector, TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private bool _baselineEstablished;
    public event EventHandler<DetectedPhysicalCart>? CartInserted;
    public event EventHandler<string>? CartRemoved;

    public void Start() => _loop ??= MonitorAsync(_stop.Token);
    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
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
            foreach (var cart in carts.Where(cart => !_known.Contains(cart.MediaRoot))) CartInserted?.Invoke(this, cart);
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
