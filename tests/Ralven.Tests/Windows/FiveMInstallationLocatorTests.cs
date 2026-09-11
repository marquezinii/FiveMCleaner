using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class FiveMInstallationLocatorTests
{
    [Fact]
    public void DetectCandidates_AcceptsCompleteCustomInstallationFromExecutable()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = CreateInstallation(temporaryDirectory, "D", "Games", "FiveM");

        var result = FiveMInstallationLocator.DetectCandidates(
        [
            new FiveMInstallationCandidate(
                Path.Combine(root, "FiveM.exe"),
                FiveMInstallationSource.Shortcut)
        ]);

        Assert.True(result.IsLegacy);
        Assert.Equal(Path.GetFullPath(root), result.Installation!.Root, ignoreCase: true);
        Assert.Equal(FiveMInstallationSource.Shortcut, result.Installation.Source);
    }

    [Fact]
    public void DetectCandidates_RejectsNamedFolderWithoutRequiredLegacyLayout()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Combine("FiveM");
        Directory.CreateDirectory(Path.Combine(root, "FiveM.app", "data"));

        var result = FiveMInstallationLocator.DetectCandidates(
        [new FiveMInstallationCandidate(root, FiveMInstallationSource.Registry)]);

        Assert.Equal(FiveMInstallationDetectionStatus.NotFound, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void DetectCandidates_RejectsPartialInstallationWithoutDataDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Combine("FiveM");
        Directory.CreateDirectory(Path.Combine(root, "FiveM.app"));
        File.WriteAllText(Path.Combine(root, "FiveM.exe"), string.Empty);

        var result = FiveMInstallationLocator.DetectCandidates(
        [new FiveMInstallationCandidate(root, FiveMInstallationSource.Registry)]);

        Assert.Equal(FiveMInstallationDetectionStatus.NotFound, result.Status);
    }

    [Fact]
    public void DetectCandidates_PrefersExplicitManualSelectionOverStaleAutomaticCandidate()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var automaticRoot = CreateInstallation(temporaryDirectory, "OldFiveM");
        var manualRoot = CreateInstallation(temporaryDirectory, "NewFiveM");

        var result = FiveMInstallationLocator.DetectCandidates(
        [
            new FiveMInstallationCandidate(automaticRoot, FiveMInstallationSource.Registry),
            new FiveMInstallationCandidate(manualRoot, FiveMInstallationSource.Manual)
        ]);

        Assert.True(result.IsLegacy);
        Assert.Equal(Path.GetFullPath(manualRoot), result.Installation!.Root, ignoreCase: true);
    }

    [Fact]
    public void DetectCandidates_DoesNotChooseBetweenEquallyTrustedInstallations()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var first = CreateInstallation(temporaryDirectory, "FiveM-A");
        var second = CreateInstallation(temporaryDirectory, "FiveM-B");

        var result = FiveMInstallationLocator.DetectCandidates(
        [
            new FiveMInstallationCandidate(first, FiveMInstallationSource.Registry),
            new FiveMInstallationCandidate(second, FiveMInstallationSource.Registry)
        ]);

        Assert.Equal(FiveMInstallationDetectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Installation);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void DetectCandidates_DoesNotLetARegistryEntrySilentlyOverrideAnotherValidAutomaticRoot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var registryRoot = CreateInstallation(temporaryDirectory, "FiveM-Old");
        var defaultRoot = CreateInstallation(temporaryDirectory, "FiveM-Current");

        var result = FiveMInstallationLocator.DetectCandidates(
        [
            new FiveMInstallationCandidate(registryRoot, FiveMInstallationSource.Registry),
            new FiveMInstallationCandidate(defaultRoot, FiveMInstallationSource.DefaultLocation)
        ]);

        Assert.Equal(FiveMInstallationDetectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Installation);
    }

    [Fact]
    public void DetectCandidates_PrefersValidatedRunningInstallationOverRegistryCandidate()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var registryRoot = CreateInstallation(temporaryDirectory, "FiveM-Old");
        var runningRoot = CreateInstallation(temporaryDirectory, "FiveM-Running");
        var childProcess = Path.Combine(runningRoot, "FiveM.app", "data", "FiveM_b3258_GTAProcess.exe");
        File.WriteAllText(childProcess, string.Empty);

        var result = FiveMInstallationLocator.DetectCandidates(
        [
            new FiveMInstallationCandidate(registryRoot, FiveMInstallationSource.Registry),
            new FiveMInstallationCandidate(childProcess, FiveMInstallationSource.RunningProcess)
        ]);

        Assert.True(result.IsLegacy);
        Assert.Equal(Path.GetFullPath(runningRoot), result.Installation!.Root, ignoreCase: true);
    }

    private static string CreateInstallation(TemporaryDirectory temporaryDirectory, params string[] parts)
    {
        var root = temporaryDirectory.Combine(parts);
        Directory.CreateDirectory(Path.Combine(root, "FiveM.app", "data"));
        File.WriteAllText(Path.Combine(root, "FiveM.exe"), string.Empty);
        return root;
    }
}
