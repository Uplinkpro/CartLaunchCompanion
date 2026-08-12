using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed class CartHostInstanceLock : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> ProcessLocks = new(StringComparer.Ordinal);
    private readonly Mutex _mutex;
    private readonly string _name;
    private bool _ownsMutex;

    private CartHostInstanceLock(Mutex mutex, string name, bool ownsMutex)
    {
        _mutex = mutex;
        _name = name;
        _ownsMutex = ownsMutex;
    }

    public static CartHostInstanceLock? TryAcquire(string? instanceSuffix = null)
    {
        var identity = Environment.UserName + "|" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..16];
        var suffix = string.IsNullOrEmpty(instanceSuffix) ? "" : "." + SanitizeSuffix(instanceSuffix);
        var name = $"CartLaunchCompanion.Host.{token}{suffix}";
        if (!ProcessLocks.TryAdd(name, 0))
            return null;

        var mutex = new Mutex(false, name);
        try
        {
            var acquired = mutex.WaitOne(0);
            if (!acquired)
            {
                mutex.Dispose();
                ProcessLocks.TryRemove(name, out _);
                return null;
            }
            return new CartHostInstanceLock(mutex, name, true);
        }
        catch (AbandonedMutexException)
        {
            return new CartHostInstanceLock(mutex, name, true);
        }
        catch
        {
            mutex.Dispose();
            ProcessLocks.TryRemove(name, out _);
            throw;
        }
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
        ProcessLocks.TryRemove(_name, out _);
    }
}
