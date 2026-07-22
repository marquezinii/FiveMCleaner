namespace FiveMCleaner.App.Services;

/// <summary>
/// Produces a conservative rolling estimate from the weighted progress already
/// reported by the optimization engine. It never invents a duration before
/// enough real execution data exists.
/// </summary>
internal sealed class ProgressTimingEstimator
{
    private const double MaximumRemainingSeconds = 2d * 60d * 60d;
    private double? smoothedRemainingSeconds;

    public TimeSpan? EstimateRemaining(TimeSpan elapsed, double progressPercent)
    {
        if (elapsed < TimeSpan.FromSeconds(2)
            || progressPercent < 3d
            || progressPercent >= 99d)
        {
            return null;
        }

        var boundedProgress = Math.Clamp(progressPercent, 3d, 98.999d);
        var rawRemainingSeconds = elapsed.TotalSeconds
            * (100d - boundedProgress)
            / boundedProgress;
        rawRemainingSeconds = Math.Clamp(rawRemainingSeconds, 1d, MaximumRemainingSeconds);

        smoothedRemainingSeconds = smoothedRemainingSeconds is null
            ? rawRemainingSeconds
            : (smoothedRemainingSeconds.Value * 0.70d) + (rawRemainingSeconds * 0.30d);

        return TimeSpan.FromSeconds(smoothedRemainingSeconds.Value);
    }

    public void Reset()
    {
        smoothedRemainingSeconds = null;
    }
}
