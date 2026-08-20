namespace FiveMCleaner.Windows.Infrastructure;

/// <summary>
/// Process-wide, time-limited cache shared by the hardware inventory
/// inspectors: their WMI/registry reads are comparatively expensive and
/// describe data that barely changes during a session. The read runs outside
/// the lock, since holding it across a slow WMI query would serialize
/// unrelated diagnostics and a concurrent miss only costs one extra read.
/// </summary>
internal sealed class TimedSnapshotCache<T>
    where T : class
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private T? cached;
    private DateTimeOffset cachedAtUtc;

    /// <summary>For readers that report failure as an empty snapshot.</summary>
    public T GetOrRead(Func<T> read) => ReadCore(read)!;

    /// <summary>
    /// For readers that report failure as null: the null is never cached, so a
    /// failed read is retried instead of being remembered for a whole TTL.
    /// </summary>
    public T? GetOrReadOptional(Func<T?> read) => ReadCore(read);

    private T? ReadCore(Func<T?> read)
    {
        lock (gate)
        {
            if (cached is not null && DateTimeOffset.UtcNow - cachedAtUtc < Ttl)
            {
                return cached;
            }
        }

        var fresh = read();

        lock (gate)
        {
            cached = fresh;
            cachedAtUtc = DateTimeOffset.UtcNow;
        }

        return fresh;
    }
}
