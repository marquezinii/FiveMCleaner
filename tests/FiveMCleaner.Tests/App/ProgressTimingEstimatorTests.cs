using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class ProgressTimingEstimatorTests
{
    [Fact]
    public void EstimateRemaining_WaitsForEnoughRealProgress()
    {
        var estimator = new ProgressTimingEstimator();

        Assert.Null(estimator.EstimateRemaining(TimeSpan.FromSeconds(1), 50));
        Assert.Null(estimator.EstimateRemaining(TimeSpan.FromSeconds(10), 2));
        Assert.Null(estimator.EstimateRemaining(TimeSpan.FromSeconds(10), 99));
    }

    [Fact]
    public void EstimateRemaining_UsesElapsedWeightedProgress()
    {
        var estimator = new ProgressTimingEstimator();

        var remaining = estimator.EstimateRemaining(TimeSpan.FromSeconds(20), 25);

        Assert.Equal(TimeSpan.FromSeconds(60), remaining);
    }

    [Fact]
    public void EstimateRemaining_SmoothsAbruptChangesAndCanReset()
    {
        var estimator = new ProgressTimingEstimator();
        _ = estimator.EstimateRemaining(TimeSpan.FromSeconds(20), 25);

        var smoothed = estimator.EstimateRemaining(TimeSpan.FromSeconds(20), 50);
        estimator.Reset();
        var afterReset = estimator.EstimateRemaining(TimeSpan.FromSeconds(20), 50);

        Assert.NotNull(smoothed);
        Assert.InRange(smoothed.Value.TotalSeconds, 47.9, 48.1);
        Assert.Equal(TimeSpan.FromSeconds(20), afterReset);
    }

    [Fact]
    public void EstimateRemaining_CapsPathologicalDurations()
    {
        var estimator = new ProgressTimingEstimator();

        var remaining = estimator.EstimateRemaining(TimeSpan.FromHours(10), 3);

        Assert.Equal(TimeSpan.FromHours(2), remaining);
    }
}
