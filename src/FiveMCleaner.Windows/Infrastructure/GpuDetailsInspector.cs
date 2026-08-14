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
/// integrated/discrete split is the name-based heuristic in
/// <see cref="GpuVendorClassifier"/>, not a hardware query, and is presented as
/// a guess rather than a fact.
/// </summary>
public sealed class WindowsGpuDetailsInspector : IGpuDetailsInspector
{
    private static readonly TimedSnapshotCache<IReadOnlyList<GpuAdapterDetails>> Cache = new();

    public IReadOnlyList<GpuAdapterDetails> GetSnapshot() => Cache.GetOrRead(Read);

    private static IReadOnlyList<GpuAdapterDetails> Read()
    {
        return GpuAdapterRegistryReader.ReadAll()
            .Select(adapter => new GpuAdapterDetails(
                adapter.DriverDescription,
                adapter.VramBytes,
                GpuVendorClassifier.GuessKind(adapter.DriverDescription)))
            .ToArray();
    }
}
