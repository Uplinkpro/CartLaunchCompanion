using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed class TrustedCartDatabase
{
    public int FormatVersion { get; set; } = 1;
    public List<TrustedCartRecord> Carts { get; set; } = [];
}

public sealed class TrustedCartRecord
{
    public string CartId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string IdentityFingerprint { get; set; } = "";
    public int MinimumSecurityVersion { get; set; } = 1;
    public bool AutoLaunchApproved { get; set; }
    public DateTimeOffset TrustedUtc { get; set; }
}

public sealed class TrustedCartStore(string databasePath)
{
    private const int MaximumBytes = 1024 * 1024;
    private readonly string _path = Path.GetFullPath(databasePath);
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 6,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private static readonly PhysicalCartJsonContext JsonContext = new(Options);

    public static string DefaultPath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, "CartLaunchCompanion", "Host", "trusted-carts.json");
    }

    public async Task<TrustedCartDatabase> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new TrustedCartDatabase();
        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > MaximumBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The trusted-cart database is invalid.");
        var bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        var database = JsonSerializer.Deserialize(bytes, JsonContext.TrustedCartDatabase)
            ?? throw new InvalidDataException("The trusted-cart database is empty.");
        Validate(database);
        return database;
    }

    public async Task TrustAsync(VerifiedCartIdentity cart, bool approveAutoLaunch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cart);
        var database = await LoadAsync(cancellationToken);
        database.Carts.RemoveAll(record => string.Equals(record.CartId, cart.Identity.CartId, StringComparison.OrdinalIgnoreCase));
        database.Carts.Add(new TrustedCartRecord
        {
            CartId = cart.Identity.CartId,
            DisplayName = cart.Identity.DisplayName,
            IdentityFingerprint = cart.Fingerprint,
            MinimumSecurityVersion = cart.Identity.SecurityVersion,
            AutoLaunchApproved = approveAutoLaunch,
            TrustedUtc = DateTimeOffset.UtcNow
        });
        await SaveAsync(database, cancellationToken);
    }

    public async Task<bool> RevokeAsync(string cartId, CancellationToken cancellationToken = default)
    {
        var database = await LoadAsync(cancellationToken);
        var removed = database.Carts.RemoveAll(record => string.Equals(record.CartId, cartId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) await SaveAsync(database, cancellationToken);
        return removed;
    }

    public static bool IsTrusted(TrustedCartDatabase database, VerifiedCartIdentity cart, bool requireAutoLaunch = false) =>
        database.Carts.Any(record =>
            string.Equals(record.CartId, cart.Identity.CartId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.IdentityFingerprint, cart.Fingerprint, StringComparison.OrdinalIgnoreCase) &&
            cart.Identity.SecurityVersion >= record.MinimumSecurityVersion &&
            (!requireAutoLaunch || record.AutoLaunchApproved));

    private async Task SaveAsync(TrustedCartDatabase database, CancellationToken cancellationToken)
    {
        Validate(database);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(database, JsonContext.TrustedCartDatabase);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("The trusted-cart database is too large.");
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(TrustedCartDatabase database)
    {
        if (database.FormatVersion != 1 || database.Carts.Count > 1_000) throw new InvalidDataException("The trusted-cart database version or size is invalid.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in database.Carts)
        {
            if (!Guid.TryParseExact(record.CartId, "D", out _) || !ids.Add(record.CartId) ||
                record.DisplayName.Length is < 1 or > 80 || record.DisplayName.Any(char.IsControl) ||
                record.IdentityFingerprint.Length != 64 || !record.IdentityFingerprint.All(Uri.IsHexDigit) ||
                record.MinimumSecurityVersion < 1 || record.TrustedUtc == default)
                throw new InvalidDataException("A trusted-cart record is invalid.");
        }
    }
}
