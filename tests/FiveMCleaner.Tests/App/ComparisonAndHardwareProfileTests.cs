using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class GtaVBenchmarkServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task RunGtaVBenchmarkAsync_RejectsIterationsOutsideOneToNine(int iterations)
    {
        var service = new AppOptimizationService(demoMode: true);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RunGtaVBenchmarkAsync(iterations));
    }

    [Fact]
    public async Task RunGtaVBenchmarkAsync_NeverRunsInDemoMode()
    {
        var service = new AppOptimizationService(demoMode: true);

        var result = await service.RunGtaVBenchmarkAsync(3);

        Assert.False(result.Succeeded);
        Assert.Equal("demo-mode", result.FailureReason);
        Assert.Empty(result.Iterations);
    }
}

public sealed class HardwareProfileSignatureTests
{
    [Fact]
    public void Compute_IsStableForTheSameInputs()
    {
        var first = HardwareProfileSignature.Compute("AMD Ryzen 7 7800X3D", ["NVIDIA GeForce RTX 4070"], 32);
        var second = HardwareProfileSignature.Compute("AMD Ryzen 7 7800X3D", ["NVIDIA GeForce RTX 4070"], 32);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_IsOrderIndependentForMultipleGpus()
    {
        var first = HardwareProfileSignature.Compute(
            "Intel Core i7-13700K", ["NVIDIA GeForce RTX 4070", "Intel(R) UHD Graphics 770"], 32);
        var second = HardwareProfileSignature.Compute(
            "Intel Core i7-13700K", ["Intel(R) UHD Graphics 770", "NVIDIA GeForce RTX 4070"], 32);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_IsCaseInsensitive()
    {
        var first = HardwareProfileSignature.Compute("amd ryzen 7 7800x3d", ["nvidia geforce rtx 4070"], 32);
        var second = HardwareProfileSignature.Compute("AMD RYZEN 7 7800X3D", ["NVIDIA GEFORCE RTX 4070"], 32);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_DiffersForDifferentHardware()
    {
        var first = HardwareProfileSignature.Compute("AMD Ryzen 7 7800X3D", ["NVIDIA GeForce RTX 4070"], 32);
        var second = HardwareProfileSignature.Compute("Intel Core i5-12400F", ["AMD Radeon RX 6600"], 16);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Compute_ToleratesSmallMemoryReadingNoiseAroundTheSameInstalledCapacity()
    {
        // Windows reports slightly less than the nominal capacity (e.g. 31.9
        // vs 32 GiB); the signature should not change because of that noise.
        var first = HardwareProfileSignature.Compute("AMD Ryzen 7 7800X3D", ["NVIDIA GeForce RTX 4070"], 31.9);
        var second = HardwareProfileSignature.Compute("AMD Ryzen 7 7800X3D", ["NVIDIA GeForce RTX 4070"], 32.0);

        Assert.Equal(first, second);
    }
}

public sealed class RegressionDetectionTests
{
    private static ResourceComparisonSnapshot Snapshot(
        double availableMemoryGiB = 8,
        bool thermalElevated = false)
    {
        return new ResourceComparisonSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            AvailableMemoryGiB = availableMemoryGiB,
            ThermalElevated = thermalElevated
        };
    }

    [Fact]
    public void ComputeRegressionReasonKeys_FlagsNewThermalSignal()
    {
        var before = Snapshot(thermalElevated: false);
        var after = Snapshot(thermalElevated: true);

        var reasons = AppOptimizationService.ComputeRegressionReasonKeys(before, after);

        Assert.Contains("Comparison.Reason.NewThermalSignal", reasons);
    }

    [Fact]
    public void ComputeRegressionReasonKeys_DoesNotFlagThermalWhenAlreadyElevatedBefore()
    {
        var before = Snapshot(thermalElevated: true);
        var after = Snapshot(thermalElevated: true);

        var reasons = AppOptimizationService.ComputeRegressionReasonKeys(before, after);

        Assert.DoesNotContain("Comparison.Reason.NewThermalSignal", reasons);
    }

    [Fact]
    public void ComputeRegressionReasonKeys_FlagsSharpMemoryDrop()
    {
        var before = Snapshot(availableMemoryGiB: 8);
        var after = Snapshot(availableMemoryGiB: 2);

        var reasons = AppOptimizationService.ComputeRegressionReasonKeys(before, after);

        Assert.Contains("Comparison.Reason.MemoryDropped", reasons);
    }

    [Fact]
    public void ComputeRegressionReasonKeys_DoesNotFlagModestMemoryVariation()
    {
        var before = Snapshot(availableMemoryGiB: 8);
        var after = Snapshot(availableMemoryGiB: 7);

        var reasons = AppOptimizationService.ComputeRegressionReasonKeys(before, after);

        Assert.Empty(reasons);
    }

    [Fact]
    public void ComputeRegressionReasonKeys_IgnoresMemoryDropWhenBeforeWasAlreadyTiny()
    {
        // Avoids false positives on machines that were already low on memory
        // before the optimization even started.
        var before = Snapshot(availableMemoryGiB: 0.5);
        var after = Snapshot(availableMemoryGiB: 0.1);

        var reasons = AppOptimizationService.ComputeRegressionReasonKeys(before, after);

        Assert.Empty(reasons);
    }

    [Fact]
    public void ComputeRegressionReasonKeys_ReturnsEmptyForAHealthyComparison()
    {
        var before = Snapshot();
        var after = Snapshot();

        var reasons = AppOptimizationService.ComputeRegressionReasonKeys(before, after);

        Assert.Empty(reasons);
    }
}
