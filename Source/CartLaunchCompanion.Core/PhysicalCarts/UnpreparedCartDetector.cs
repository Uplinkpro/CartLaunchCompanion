namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record UnpreparedCartCandidate(
    string MediaRoot,
    string SuggestedName,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> MissingFolders);

public sealed class UnpreparedCartDetector(IMountRootProvider mountRoots)
{
    private static readonly (string Platform, string EntryPoint)[] RuntimeEntries =
    [
        ("Windows-x64", "CartLaunchCompanion.Desktop.exe"),
        ("Linux-x64", "CartLaunchCompanion.Desktop")
    ];

    public IReadOnlyList<UnpreparedCartCandidate> Scan()
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var found = new List<UnpreparedCartCandidate>();
        var seen = new HashSet<string>(comparison);
        foreach (var candidate in mountRoots.GetMountedRoots().Take(512))
        {
            try
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
                if (!seen.Add(root) || HasIdentityEntry(root)) continue;
                var cart = new DirectoryInfo(Path.Combine(root, "Cart"));
                if (!cart.Exists || IsLink(cart)) continue;

                var platforms = RuntimeEntries
                    .Where(entry => IsRegularRuntimeEntry(Path.Combine(cart.FullName, "System", entry.Platform, entry.EntryPoint)))
                    .Select(entry => entry.Platform)
                    .ToArray();
                if (platforms.Length == 0) continue;

                var missing = new[] { "Games", "Emulators", "Roms" }
                    .Where(folder => !Directory.Exists(Path.Combine(root, folder)))
                    .ToArray();
                found.Add(new(root, GetSuggestedName(root), platforms, missing));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                // Unreadable and malformed media must remain silent.
            }
        }
        return found;
    }

    private static bool IsRegularRuntimeEntry(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && !IsLink(file) && file.Length > 0;
    }

    private static bool IsLink(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;

    private static bool HasIdentityEntry(string root)
    {
        var path = CartIdentityService.GetIdentityPath(root);
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        if (directory.Exists && IsLink(directory)) return true;
        var file = new FileInfo(path);
        return file.Exists || file.LinkTarget is not null || Directory.Exists(path);
    }

    private static string GetSuggestedName(string root)
    {
        try
        {
            var label = new DriveInfo(root).VolumeLabel.Trim();
            if (!string.IsNullOrWhiteSpace(label)) return label.Length <= 80 ? label : label[..80];
        }
        catch { }
        var name = Path.GetFileName(root);
        return string.IsNullOrWhiteSpace(name) ? "My Game Cart" : name[..Math.Min(name.Length, 80)];
    }
}

public sealed class UnpreparedCartMonitor(
    UnpreparedCartDetector detector,
    TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private HashSet<string> _known = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _baselineEstablished;
    public event EventHandler<UnpreparedCartCandidate>? CandidateInserted;

    public void Start() => _loop ??= MonitorAsync(_stop.Token);

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            IReadOnlyList<UnpreparedCartCandidate> candidates;
            try { candidates = detector.Scan(); }
            catch (OperationCanceledException) { break; }
            var current = candidates.Select(item => item.MediaRoot).ToHashSet(_known.Comparer);
            if (!_baselineEstablished)
            {
                _known = current;
                _baselineEstablished = true;
                continue;
            }
            foreach (var candidate in candidates.Where(item => !_known.Contains(item.MediaRoot)))
                CandidateInserted?.Invoke(this, candidate);
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
