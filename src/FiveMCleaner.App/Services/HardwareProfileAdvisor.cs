using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

internal sealed record HardwareProfileAssessment(
    int ReadinessScore,
    OptimizationProfile RecommendedProfile,
    PerformancePressureLevel PerformancePressure);

/// <summary>
/// Produces a transparent capacity assessment from values read locally. This
/// is not an FPS benchmark and deliberately does not claim a numeric gain.
/// </summary>
internal static class HardwareProfileAdvisor
{
    private const long LargeCacheThresholdBytes = 8L * 1024 * 1024 * 1024;

    public static HardwareProfileAssessment Assess(
        double totalMemoryGiB,
        double availableMemoryGiB,
        int logicalProcessorCount,
        double freeDiskGiB,
        FiveMEdition edition,
        long legacyCacheBytes,
        bool gpuWasIdentified)
    {
        var processors = Math.Max(1, logicalProcessorCount);
        var score = totalMemoryGiB >= 16 ? 22 : totalMemoryGiB >= 8 ? 14 : 6;
        score += availableMemoryGiB >= 8 ? 10 : availableMemoryGiB >= 4 ? 7 : availableMemoryGiB >= 2 ? 4 : 1;
        score += processors >= 12 ? 15 : processors >= 8 ? 12 : processors >= 4 ? 8 : 4;
        score += freeDiskGiB >= 30 ? 15 : freeDiskGiB >= 15 ? 10 : freeDiskGiB >= 8 ? 5 : 2;
        score += edition == FiveMEdition.Legacy ? 25 : edition == FiveMEdition.Enhanced ? 8 : 0;
        score += gpuWasIdentified ? 8 : 3;
        score += legacyCacheBytes < LargeCacheThresholdBytes ? 5 : 2;
        score = Math.Clamp(score, 0, 100);

        var pressurePoints = 0;
        pressurePoints += totalMemoryGiB < 12 ? 3 : 0;
        pressurePoints += availableMemoryGiB < 3 ? 2 : 0;
        pressurePoints += processors <= 4 ? 2 : 0;
        pressurePoints += freeDiskGiB < 12 ? 2 : 0;
        pressurePoints += gpuWasIdentified ? 0 : 1;
        var pressure = pressurePoints >= 4
            ? PerformancePressureLevel.High
            : pressurePoints >= 1
                ? PerformancePressureLevel.Moderate
                : PerformancePressureLevel.Low;

        var recommendation = pressure == PerformancePressureLevel.High
            ? OptimizationProfile.Aggressive
            : pressure == PerformancePressureLevel.Low
              && totalMemoryGiB >= 24
              && availableMemoryGiB >= 8
              && processors >= 12
              && freeDiskGiB >= 30
                ? OptimizationProfile.Light
                : OptimizationProfile.Balanced;

        return new HardwareProfileAssessment(score, recommendation, pressure);
    }
}
