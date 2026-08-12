using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed class CartIdentityService
{
    public const string FileName = "cartlaunch.cartridge.json";
    public const int MaximumBytes = 16 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 4,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private static readonly PhysicalCartJsonContext JsonContext = new(Options);

    public CartIdentity Create(string displayName) => new()
    {
        CartId = Guid.NewGuid().ToString("D"),
        DisplayName = ValidateDisplayName(displayName),
        CreatedUtc = DateTimeOffset.UtcNow
    };

    public async Task<VerifiedCartIdentity> LoadAsync(string mediaRoot, CancellationToken cancellationToken = default)
    {
        var path = ResolveIdentityPath(mediaRoot);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("This media is not a Cart Launch cart.", path);
        if (info.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("The cart identity has an invalid size.");
        RejectLink(info);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var identity = JsonSerializer.Deserialize(bytes, JsonContext.CartIdentity)
            ?? throw new InvalidDataException("The cart identity is empty.");
        Validate(identity);
        return new VerifiedCartIdentity(identity, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public async Task<VerifiedCartIdentity> SaveNewAsync(string mediaRoot, CartIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Validate(identity);
        var path = ResolveIdentityPath(mediaRoot);
        if (File.Exists(path)) throw new IOException("This media already has a cart identity.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(identity, JsonContext.CartIdentity);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The cart identity is too large.");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return new VerifiedCartIdentity(identity, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static string ResolveIdentityPath(string mediaRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The media root does not exist.");
        var rootInfo = new DirectoryInfo(root);
        RejectLink(rootInfo);
        return Path.Combine(root, FileName);
    }

    private static void Validate(CartIdentity identity)
    {
        if (identity.FormatVersion != 1 || identity.SecurityVersion != 1)
            throw new InvalidDataException("The cart identity version is unsupported.");
        if (!Guid.TryParseExact(identity.CartId, "D", out _))
            throw new InvalidDataException("The cart ID is invalid.");
        identity.DisplayName = ValidateDisplayName(identity.DisplayName);
        if (identity.CreatedUtc == default || identity.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("The cart creation time is invalid.");
    }

    private static string ValidateDisplayName(string value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new InvalidDataException("The cart name must be 1–80 printable characters.");
        return name;
    }

    private static void RejectLink(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new InvalidDataException("Links are not allowed in cart identity paths.");
    }
}
