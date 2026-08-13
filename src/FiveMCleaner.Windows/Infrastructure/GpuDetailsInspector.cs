namespace FiveMCleaner.Windows.Infrastructure;

public enum GpuKindGuess
{
    Unknown,
    LikelyIntegrated,
    LikelyDiscrete
}

public sealed record GpuAdapterDetails(
    string DriverDescription,
    long? VramBytes,
    GpuKindGuess KindGuess);

public interface IGpuDetailsInspector
{
    IReadOnlyList<GpuAdapterDetails> GetSnapshot();
}

/// <summary>
/// Reads VRAM size and a best-effort integrated-vs-discrete classification
/// from the same registry location already used for GPU driver descriptions
/// (SYSTEM\CurrentControlSet\Control\Video). VRAM comes from the
/// HardwareInformation.qwMemorySize value most drivers publish; the
/// integrated/discrete split is a name-based heuristic, not a hardware
/// query, and is presented as a guess rather than a fact.
///
/// Caches results for 30 seconds to avoid repeated registry queries during a single session.
/// </summary>
public sealed class WindowsGpuDetailsInspector : IGpuDetailsInspector
{
    private static readonly string[] IntegratedMarkers =
    [
        "Intel(R) UHD",
        "Intel(R) HD Graphics",
        "Intel(R) Iris",
        "AMD Radeon(TM) Graphics",
        "AMD Radeon Graphics",
        "Radeon(TM) Vega"
    ];

    private static readonly object CacheLock = new();
    private static IReadOnlyList<GpuAdapterDetails>? cachedSnapshot;
    private static DateTimeOffset? cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public IReadOnlyList<GpuAdapterDetails> GetSnapshot()
    {
        // Check cache first
        lock (CacheLock)
        {
            if (cachedSnapshot is not null && cachedAt is not null &&
                DateTimeOffset.UtcNow - cachedAt.Value < CacheTtl)
            {
                return cachedSnapshot;
            }
        }

        var snapshot = GetSnapshotInternal();

        // Update cache
        lock (CacheLock)
        {
            cachedSnapshot = snapshot;
            cachedAt = DateTimeOffset.UtcNow;
        }

        return snapshot;
    }

    private static IReadOnlyList<GpuAdapterDetails> GetSnapshotInternal()
    {
        return GpuAdapterRegistryReader.ReadAll()
            .Select(adapter => new GpuAdapterDetails(
                adapter.DriverDescription,
                adapter.VramBytes,
                GuessKind(adapter.DriverDescription)))
            .ToArray();
    }

    private static GpuKindGuess GuessKind(string driverDescription)
    {
        if (IntegratedMarkers.Any(marker =>
                driverDescription.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return GpuKindGuess.LikelyIntegrated;
        }

        if (driverDescription.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || driverDescription.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)
            || driverDescription.Contains("Arc", StringComparison.OrdinalIgnoreCase))
        {
            return GpuKindGuess.LikelyDiscrete;
        }

        return GpuKindGuess.Unknown;
    }
}
