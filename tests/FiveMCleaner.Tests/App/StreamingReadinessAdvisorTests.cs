using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class StreamingReadinessAdvisorTests
{
    [Fact]
    public void Evaluate_PreservesRunningStreamingSoftwareWithoutClaimingLiveState()
    {
        var diagnostic = CreateDiagnostic(
            streamingSoftware: StreamingSoftwareClassifier.CreateSnapshot(
                runningProcessNames: ["obs64"],
                installedProductNames: [],
                installedExecutableKinds: [],
                observedAtUtc: DateTimeOffset.UtcNow));

        var assessment = StreamingReadinessAdvisor.Evaluate(diagnostic);

        Assert.Equal(StreamingReadinessLevel.Protected, assessment.Level);
        var software = Assert.Single(assessment.Checks, check =>
            check.Kind == StreamingReadinessCheckKind.Software);
        Assert.Equal(StreamingReadinessTone.Protected, software.Tone);
        Assert.Equal(["OBS Studio"], software.ApplicationNames);
        Assert.DoesNotContain("live", software.ApplicationNames[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_RequestsTestWhenMeasuredResourcesAreConstrained()
    {
        var diagnostic = CreateDiagnostic(
            availableMemoryGiB: 2.5,
            performancePressure: PerformancePressureLevel.High);

        var assessment = StreamingReadinessAdvisor.Evaluate(diagnostic);

        Assert.Equal(StreamingReadinessLevel.Attention, assessment.Level);
        var resources = Assert.Single(assessment.Checks, check =>
            check.Kind == StreamingReadinessCheckKind.Resources);
        Assert.Equal(StreamingReadinessTone.Caution, resources.Tone);
    }

    [Fact]
    public void Evaluate_UsesPartialStateWhenStreamingScanWasNotComplete()
    {
        var diagnostic = CreateDiagnostic(
            streamingSoftware: StreamingSoftwareClassifier.CreateSnapshot(
                runningProcessNames: [],
                installedProductNames: [],
                installedExecutableKinds: [],
                observedAtUtc: DateTimeOffset.UtcNow,
                processScanComplete: false,
                installationScanComplete: true));

        var assessment = StreamingReadinessAdvisor.Evaluate(diagnostic);

        Assert.Equal(StreamingReadinessLevel.Partial, assessment.Level);
    }

    [Fact]
    public void Evaluate_ReportsSafePlanningWhenGamesAreClosed()
    {
        var assessment = StreamingReadinessAdvisor.Evaluate(CreateDiagnostic());

        var gameSession = Assert.Single(assessment.Checks, check =>
            check.Kind == StreamingReadinessCheckKind.GameSession);
        Assert.Equal(StreamingReadinessTone.Ready, gameSession.Tone);
    }

    private static AppDiagnostic CreateDiagnostic(
        double availableMemoryGiB = 8,
        PerformancePressureLevel performancePressure = PerformancePressureLevel.Low,
        StreamingSoftwareSnapshot? streamingSoftware = null)
    {
        return new AppDiagnostic
        {
            Edition = FiveMEdition.Legacy,
            IsFiveMRunning = false,
            FiveMRoot = null,
            GtaVDetected = true,
            GtaVIsRunning = false,
            GtaVExecutablePath = null,
            GtaVGraphicsSettingsPath = "settings.xml",
            CpuName = "Test CPU",
            GpuName = "Test GPU",
            TotalMemoryGiB = 16,
            AvailableMemoryGiB = availableMemoryGiB,
            LogicalProcessorCount = 8,
            FreeDiskGiB = 100,
            LegacyCacheBytes = 0,
            OsLabel = "Windows 11",
            ReadinessScore = 90,
            RecommendedProfile = OptimizationProfile.Balanced,
            PerformancePressure = performancePressure,
            StreamingSoftware = streamingSoftware ?? StreamingSoftwareClassifier.CreateSnapshot(
                runningProcessNames: [],
                installedProductNames: [],
                installedExecutableKinds: [],
                observedAtUtc: DateTimeOffset.UtcNow)
        };
    }
}
