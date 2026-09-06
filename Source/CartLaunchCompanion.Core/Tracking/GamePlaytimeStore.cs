using System.Text.Json;

namespace CartLaunchCompanion.Core.Tracking;

public sealed record GamePlaytimeRecord(
    long TotalSeconds,
    DateTimeOffset? LastPlayed);

public sealed class GamePlaytimeStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<GamePlaytimeRecord?> GetAsync(
        string gameId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(cancellationToken);
            return records.GetValueOrDefault(gameId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GamePlaytimeRecord> RecordSessionAsync(
        string gameId,
        TimeSpan duration,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var elapsedSeconds = Math.Max(0L, (long)Math.Round(duration.TotalSeconds));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(cancellationToken);
            var existing = records.GetValueOrDefault(gameId);
            var updated = new GamePlaytimeRecord(
                Math.Max(0L, existing?.TotalSeconds ?? 0L) + elapsedSeconds,
                endedAt.ToUniversalTime());
            records[gameId] = updated;
            await SaveAsync(records, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GamePlaytimeRecord> ImportSteamAsync(
        string gameId,
        int playtimeMinutes,
        DateTimeOffset? lastPlayed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var steamSeconds = Math.Max(0L, playtimeMinutes * 60L);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(cancellationToken);
            var existing = records.GetValueOrDefault(gameId);
            var updated = new GamePlaytimeRecord(
                Math.Max(existing?.TotalSeconds ?? 0L, steamSeconds),
                Latest(existing?.LastPlayed, lastPlayed));
            records[gameId] = updated;
            await SaveAsync(records, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, GamePlaytimeRecord>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new Dictionary<string, GamePlaytimeRecord>(StringComparer.OrdinalIgnoreCase);

        await using var stream = File.OpenRead(path);
        var records = await JsonSerializer.DeserializeAsync<Dictionary<string, GamePlaytimeRecord>>(
            stream,
            JsonOptions,
            cancellationToken);
        return records is null
            ? new Dictionary<string, GamePlaytimeRecord>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GamePlaytimeRecord>(records, StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveAsync(
        Dictionary<string, GamePlaytimeRecord> records,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The playtime file has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static DateTimeOffset? Latest(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null) return second?.ToUniversalTime();
        if (second is null) return first.Value.ToUniversalTime();
        return first.Value >= second.Value
            ? first.Value.ToUniversalTime()
            : second.Value.ToUniversalTime();
    }
}
