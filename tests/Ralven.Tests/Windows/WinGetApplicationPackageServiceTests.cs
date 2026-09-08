using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class WinGetApplicationPackageServiceTests
{
    private static readonly string WinGetPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "WindowsApps",
        "winget.exe");

    [Fact]
    public void ParseUpdates_ParsesTheFixedWidthTableAndRejectsTruncatedIds()
    {
        var packages = WinGetApplicationPackageService.ParseUpdates(
            """
            Name                 Id                    Version  Available  Source
            ---------------------------------------------------------------------
            OBS Studio           OBSProject.OBSStudio  31.0     32.1.2     winget
            Truncated package    invalid…              1.0      2.0        winget
            """,
            "winget");

        var package = Assert.Single(packages);
        Assert.Equal("OBSProject.OBSStudio", package.PackageId);
        Assert.Equal("31.0", package.Version);
        Assert.Equal("32.1.2", package.AvailableVersion);
        Assert.Equal("winget", package.Source);
    }

    [Fact]
    public async Task SearchAsync_UsesOnlyTrustedSourcesAndReportsAPartialResult()
    {
        var runner = new RecordingCommandRunner(
            new CommandResult(
                0,
                """
                Name                 Id                    Version  Source
                ----------------------------------------------------------
                VLC media player     VideoLAN.VLC          3.0.22   winget
                """,
                string.Empty),
            new CommandResult(1, string.Empty, "source unavailable"));
        var service = new WinGetApplicationPackageService(runner, WinGetPath);

        var snapshot = await service.SearchAsync(
            "VLC",
            TestContext.Current.CancellationToken);

        Assert.True(snapshot.IsWinGetAvailable);
        Assert.True(snapshot.IsPartial);
        Assert.Equal(["msstore"], snapshot.UnavailableSources);
        Assert.Equal("VideoLAN.VLC", Assert.Single(snapshot.Packages).PackageId);
        Assert.Collection(
            runner.Calls,
            call => Assert.Equal(
                [
                    "search", "--query", "VLC", "--source", "winget", "--count", "50",
                    "--disable-interactivity"
                ],
                call),
            call => Assert.Equal(
                [
                    "search", "--query", "VLC", "--source", "msstore", "--count", "50",
                    "--disable-interactivity"
                ],
                call));
    }

    [Fact]
    public async Task ExecuteAsync_UsesAnExactValidatedIdAndKeepsSecurityChecksEnabled()
    {
        var runner = new RecordingCommandRunner(
            new CommandResult(0, string.Empty, string.Empty));
        var service = new WinGetApplicationPackageService(runner, WinGetPath);
        var package = new WindowsApplicationPackage(
            "VideoLAN.VLC",
            "VLC media player",
            "3.0.21",
            "3.0.22",
            "winget");

        var result = await service.ExecuteAsync(
            WindowsApplicationPackageOperation.Update,
            package,
            TestContext.Current.CancellationToken);

        Assert.Equal(WindowsApplicationPackageOutcome.Succeeded, result.Outcome);
        var arguments = Assert.Single(runner.Calls);
        Assert.Equal(
            [
                "upgrade", "--id", "VideoLAN.VLC", "--exact", "--source", "winget",
                "--accept-package-agreements", "--accept-source-agreements",
                "--disable-interactivity"
            ],
            arguments);
        Assert.DoesNotContain("--ignore-security-hash", arguments);
        Assert.DoesNotContain("--allow-reboot", arguments);
        Assert.DoesNotContain("--force", arguments);
    }

    [Theory]
    [InlineData("VideoLAN.VLC;shutdown", "winget")]
    [InlineData("VideoLAN.VLC", "untrusted")]
    public async Task ExecuteAsync_RejectsUntrustedPackageInput(string packageId, string source)
    {
        var runner = new RecordingCommandRunner(
            new CommandResult(0, string.Empty, string.Empty));
        var service = new WinGetApplicationPackageService(runner, WinGetPath);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(
            WindowsApplicationPackageOperation.Install,
            new WindowsApplicationPackage(packageId, "Test", "1.0", null, source),
            TestContext.Current.CancellationToken));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Constructor_RejectsExecutableOutsideTheOfficialWinGetAlias()
    {
        Assert.Throws<ArgumentException>(() => new WinGetApplicationPackageService(
            new RecordingCommandRunner(),
            Path.Combine(Path.GetTempPath(), "winget.exe")));
    }

    [Fact]
    public void ParsePackages_WhenSuccessfulOutputHasNoTable_ReturnsAnEmptyList()
    {
        var packages = WinGetApplicationPackageService.ParsePackages(
            "No installed package found matching input criteria.",
            "winget");

        Assert.Empty(packages);
    }

    private sealed class RecordingCommandRunner(params CommandResult[] results) : ICommandRunner
    {
        private readonly Queue<CommandResult> results = new(results);

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<CommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Assert.Equal(WinGetPath, executable);
            Calls.Add(arguments);
            return Task.FromResult(results.Dequeue());
        }
    }
}
