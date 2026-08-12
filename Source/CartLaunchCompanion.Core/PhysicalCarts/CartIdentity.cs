namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed class CartIdentity
{
    public int FormatVersion { get; set; } = 1;
    public int SecurityVersion { get; set; } = 1;
    public string CartId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed record VerifiedCartIdentity(CartIdentity Identity, string Fingerprint);
