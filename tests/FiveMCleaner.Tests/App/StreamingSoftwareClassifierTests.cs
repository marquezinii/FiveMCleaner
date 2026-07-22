using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class StreamingSoftwareClassifierTests
{
    [Theory]
    [InlineData("obs64", StreamingSoftwareKind.ObsStudio)]
    [InlineData("OBS32.EXE", StreamingSoftwareKind.ObsStudio)]
    [InlineData(" Streamlabs Desktop.exe ", StreamingSoftwareKind.StreamlabsDesktop)]
    [InlineData("TikTok LIVE Studio", StreamingSoftwareKind.TikTokLiveStudio)]
    [InlineData("TikTokLiveStudio.exe", StreamingSoftwareKind.TikTokLiveStudio)]
    public void ProcessClassifier_AcceptsOnlyAllowlistedExecutableNames(
        string processName,
        StreamingSoftwareKind expected)
    {
        Assert.Equal(
            expected,
            StreamingSoftwareClassifier.ClassifyProcessName(processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("obs-browser-page")]
    [InlineData("obs64-helper")]
    [InlineData("Streamlabs Chatbot")]
    [InlineData("TikTok")]
    [InlineData(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe")]
    public void ProcessClassifier_RejectsLookalikesHelpersAndPaths(string? processName)
    {
        Assert.Null(StreamingSoftwareClassifier.ClassifyProcessName(processName));
    }

    [Theory]
    [InlineData("OBS Studio", StreamingSoftwareKind.ObsStudio)]
    [InlineData("obs studio (64BIT)", StreamingSoftwareKind.ObsStudio)]
    [InlineData("Streamlabs Desktop", StreamingSoftwareKind.StreamlabsDesktop)]
    [InlineData("TikTok LIVE Studio", StreamingSoftwareKind.TikTokLiveStudio)]
    public void InstalledProductClassifier_AcceptsOnlyKnownDisplayNames(
        string displayName,
        StreamingSoftwareKind expected)
    {
        Assert.Equal(
            expected,
            StreamingSoftwareClassifier.ClassifyInstalledProductName(displayName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OBS Studio Plugin")]
    [InlineData("OBS Studio 99.0 malware")]
    [InlineData("Streamlabs")]
    [InlineData("TikTok Studio Helper")]
    public void InstalledProductClassifier_RejectsUnknownOrPrefixedProducts(
        string? displayName)
    {
        Assert.Null(
            StreamingSoftwareClassifier.ClassifyInstalledProductName(displayName));
    }

    [Fact]
    public void Snapshot_PreservesInstalledAndRunningAsIndependentSignals()
    {
        var observedAt = new DateTimeOffset(
            2026,
            7,
            18,
            12,
            30,
            0,
            TimeSpan.FromHours(-3));

        var snapshot = StreamingSoftwareClassifier.CreateSnapshot(
            runningProcessNames: ["obs64", "unrelated-process"],
            installedProductNames: ["TikTok LIVE Studio", "Unknown Broadcaster"],
            installedExecutableKinds: [StreamingSoftwareKind.StreamlabsDesktop],
            observedAtUtc: observedAt);

        Assert.Collection(
            snapshot.Applications,
            obs =>
            {
                Assert.Equal(StreamingSoftwareKind.ObsStudio, obs.Kind);
                Assert.False(obs.IsInstalled);
                Assert.True(obs.IsProcessRunning);
                Assert.True(obs.IsDetected);
            },
            streamlabs =>
            {
                Assert.Equal(StreamingSoftwareKind.StreamlabsDesktop, streamlabs.Kind);
                Assert.True(streamlabs.IsInstalled);
                Assert.False(streamlabs.IsProcessRunning);
            },
            tiktok =>
            {
                Assert.Equal(StreamingSoftwareKind.TikTokLiveStudio, tiktok.Kind);
                Assert.True(tiktok.IsInstalled);
                Assert.False(tiktok.IsProcessRunning);
            });

        Assert.True(snapshot.HasKnownSoftwareInstalled);
        Assert.True(snapshot.HasKnownProcessRunning);
        Assert.False(snapshot.IsPartial);
        Assert.Equal(observedAt.ToUniversalTime(), snapshot.ObservedAtUtc);
    }

    [Fact]
    public void Snapshot_ReportsPartialCoverageInsteadOfAssumingAbsence()
    {
        var snapshot = StreamingSoftwareClassifier.CreateSnapshot(
            runningProcessNames: [],
            installedProductNames: [],
            installedExecutableKinds: [],
            observedAtUtc: DateTimeOffset.UnixEpoch,
            processScanComplete: false,
            installationScanComplete: true);

        Assert.True(snapshot.IsPartial);
        Assert.False(snapshot.ProcessScanComplete);
        Assert.True(snapshot.InstallationScanComplete);
        Assert.All(snapshot.Applications, application =>
        {
            Assert.False(application.IsInstalled);
            Assert.False(application.IsProcessRunning);
        });
    }

    [Fact]
    public void Snapshot_IgnoresUndefinedKindsFromExecutableEvidence()
    {
        var snapshot = StreamingSoftwareClassifier.CreateSnapshot(
            runningProcessNames: [],
            installedProductNames: [],
            installedExecutableKinds: [(StreamingSoftwareKind)999],
            observedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.False(snapshot.HasKnownSoftwareInstalled);
    }
}
