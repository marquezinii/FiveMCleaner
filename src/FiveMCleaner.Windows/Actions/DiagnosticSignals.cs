using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Thresholds and derived signals used by more than one read-only diagnostic,
/// so the same reading cannot be called "elevated" by one action and "normal"
/// by another.
/// </summary>
internal static class DiagnosticSignals
{
    internal const long GiB = 1024L * 1024L * 1024L;

    // Conservative generic threshold: without a per-core sensor this product
    // can only say throttling is plausible, never that it happened.
    private const double ElevatedTemperatureCelsius = 85d;

    // Free-memory fraction plus an absolute floor, since the fraction alone is
    // too permissive on machines with a lot of RAM.
    private const double LowAvailableMemoryRatio = 0.10d;
    private const long LowAvailableMemoryBytes = 1536L * 1024 * 1024;

    internal static bool IsTemperatureElevated(ThermalSnapshot snapshot)
    {
        return snapshot is { IsAvailable: true, HighestCelsius: >= ElevatedTemperatureCelsius };
    }

    internal static bool IsMemoryUnderPressure(SystemResourceSnapshot snapshot)
    {
        var availableRatio = snapshot.TotalMemoryBytes > 0
            ? (double)snapshot.AvailableMemoryBytes / snapshot.TotalMemoryBytes
            : 1d;
        return availableRatio < LowAvailableMemoryRatio
            || snapshot.AvailableMemoryBytes < LowAvailableMemoryBytes;
    }
}
