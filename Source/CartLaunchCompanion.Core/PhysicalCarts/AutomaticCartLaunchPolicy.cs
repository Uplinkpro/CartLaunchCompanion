namespace CartLaunchCompanion.Core.PhysicalCarts;

public enum AutomaticLaunchDecision { Allowed, NotTrusted, NotApproved, AlreadyActive, RateLimited }

public sealed class AutomaticCartLaunchPolicy(TimeSpan? retryCooldown = null)
{
    private readonly TimeSpan _retryCooldown = retryCooldown ?? TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastAttempt = new(StringComparer.OrdinalIgnoreCase);

    public AutomaticLaunchDecision TryBegin(TrustedCartDatabase database, VerifiedCartIdentity cart, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!TrustedCartStore.IsTrusted(database, cart)) return AutomaticLaunchDecision.NotTrusted;
            if (!TrustedCartStore.IsTrusted(database, cart, requireAutoLaunch: true)) return AutomaticLaunchDecision.NotApproved;
            if (_active.Contains(cart.Identity.CartId)) return AutomaticLaunchDecision.AlreadyActive;
            if (_lastAttempt.TryGetValue(cart.Identity.CartId, out var last) && now - last < _retryCooldown) return AutomaticLaunchDecision.RateLimited;
            _active.Add(cart.Identity.CartId);
            _lastAttempt[cart.Identity.CartId] = now;
            return AutomaticLaunchDecision.Allowed;
        }
    }

    public void Complete(string cartId)
    {
        lock (_sync) _active.Remove(cartId);
    }

    public bool IsActive(string cartId)
    {
        lock (_sync) return _active.Contains(cartId);
    }
}
