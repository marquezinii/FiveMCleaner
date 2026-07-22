using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class HardwareProfileAdvisorTests
{
    [Fact]
    public void Assess_RecommendsLightOnlyForLowPressureHighCapacityPc()
    {
        var result = HardwareProfileAdvisor.Assess(
            totalMemoryGiB: 32,
            availableMemoryGiB: 12,
            logicalProcessorCount: 16,
            freeDiskGiB: 100,
            FiveMEdition.Legacy,
            legacyCacheBytes: 2L * 1024 * 1024 * 1024,
            gpuWasIdentified: true);

        Assert.Equal(OptimizationProfile.Light, result.RecommendedProfile);
        Assert.Equal(PerformancePressureLevel.Low, result.PerformancePressure);
        Assert.Equal(100, result.ReadinessScore);
    }

    [Fact]
    public void Assess_RecommendsAggressiveForSeveralMeasuredConstraints()
    {
        var result = HardwareProfileAdvisor.Assess(
            totalMemoryGiB: 8,
            availableMemoryGiB: 1.5,
            logicalProcessorCount: 4,
            freeDiskGiB: 9,
            FiveMEdition.Legacy,
            legacyCacheBytes: 10L * 1024 * 1024 * 1024,
            gpuWasIdentified: true);

        Assert.Equal(OptimizationProfile.Aggressive, result.RecommendedProfile);
        Assert.Equal(PerformancePressureLevel.High, result.PerformancePressure);
        Assert.InRange(result.ReadinessScore, 0, 99);
    }

    [Fact]
    public void Assess_UsesBalancedForTypicalPcWithoutInventingAHighEndResult()
    {
        var result = HardwareProfileAdvisor.Assess(
            totalMemoryGiB: 16,
            availableMemoryGiB: 6,
            logicalProcessorCount: 8,
            freeDiskGiB: 40,
            FiveMEdition.Legacy,
            legacyCacheBytes: 1,
            gpuWasIdentified: true);

        Assert.Equal(OptimizationProfile.Balanced, result.RecommendedProfile);
        Assert.Equal(PerformancePressureLevel.Low, result.PerformancePressure);
    }

    [Fact]
    public void Assess_PenalizesUnknownEditionAndGpuWithoutThrowing()
    {
        var result = HardwareProfileAdvisor.Assess(
            totalMemoryGiB: 16,
            availableMemoryGiB: 6,
            logicalProcessorCount: 8,
            freeDiskGiB: 40,
            FiveMEdition.Unknown,
            legacyCacheBytes: 0,
            gpuWasIdentified: false);

        Assert.InRange(result.ReadinessScore, 0, 74);
        Assert.Equal(OptimizationProfile.Balanced, result.RecommendedProfile);
    }
}
