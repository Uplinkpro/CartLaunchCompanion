using System.Security.Cryptography;
using System.Text;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed class CartHostInstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private CartHostInstanceLock(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static CartHostInstanceLock? TryAcquire(string? instanceSuffix = null)
    {
        var identity = Environment.UserName + "|" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..16];
        var suffix = string.IsNullOrEmpty(instanceSuffix) ? "" : "." + SanitizeSuffix(instanceSuffix);
        var mutex = new Mutex(false, $"CartLaunchCompanion.Host.{token}{suffix}");
        try
        {
            var acquired = mutex.WaitOne(0);
            if (!acquired) { mutex.Dispose(); return null; }
            return new CartHostInstanceLock(mutex, true);
        }
        catch (AbandonedMutexException) { return new CartHostInstanceLock(mutex, true); }
    }

    private static string SanitizeSuffix(string value)
    {
        if (value.Length > 64 || value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("The instance suffix is invalid.", nameof(value));
        return value;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
