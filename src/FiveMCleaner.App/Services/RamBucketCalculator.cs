namespace FiveMCleaner.App.Services;

/// <summary>
/// Rounds an exact RAM reading up to the nearest allowlisted bucket before
/// it can ever reach telemetry, so the value stays a coarse hardware
/// category (matching what <see cref="TelemetryEventValidator"/> accepts)
/// instead of a precise, potentially more identifying number.
/// </summary>
public static class RamBucketCalculator
{
    private static readonly int[] AllowedBucketsGiB = [2, 4, 8, 16, 32, 64, 128, 256];

    public static int ComputeBucketGiB(double totalMemoryGiB)
    {
        foreach (var bucket in AllowedBucketsGiB)
        {
            if (totalMemoryGiB <= bucket)
            {
                return bucket;
            }
        }

        return AllowedBucketsGiB[^1];
    }
}
