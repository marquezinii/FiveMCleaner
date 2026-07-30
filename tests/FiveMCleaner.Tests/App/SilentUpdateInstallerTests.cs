using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class SilentUpdateInstallerTests : IDisposable
{
    private readonly string updatesRoot = Path.Combine(
        Path.GetTempPath(),
        "FiveMCleanerTests",
        Guid.NewGuid().ToString("N"));

    public SilentUpdateInstallerTests()
    {
        Directory.CreateDirectory(updatesRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(updatesRoot))
        {
            Directory.Delete(updatesRoot, recursive: true);
        }
    }

    private DownloadedUpdate CreateVerifiedInstaller(string version = "9.9.9")
    {
        var path = Path.Combine(updatesRoot, $"FiveMCleaner-Setup-{version}-win-x64.exe");
        File.WriteAllText(path, "fake installer bytes");
        return new DownloadedUpdate(
            StableSemanticVersion.Parse(version),
            path,
            SizeBytes: new FileInfo(path).Length,
            WasAlreadyDownloaded: false);
    }

    [Fact]
    public void BuildArguments_AlwaysIncludesSilentAndAutoUpdateFlags()
    {
        var installer = new SilentUpdateInstaller(updatesRoot);

        var arguments = installer.BuildArguments();

        Assert.Contains("/VERYSILENT", arguments);
        Assert.Contains("/SUPPRESSMSGBOXES", arguments);
        Assert.Contains("/NORESTART", arguments);
        Assert.Contains("/AUTOUPDATE=yes", arguments);
    }

    [Fact]
    public void BuildArguments_IncludesALogPathOnlyWhenALogDirectoryWasConfigured()
    {
        var withoutLog = new SilentUpdateInstaller(updatesRoot);
        var withLog = new SilentUpdateInstaller(updatesRoot, logDirectory: updatesRoot);

        Assert.DoesNotContain(withoutLog.BuildArguments(), argument => argument.StartsWith("/LOG=", StringComparison.Ordinal));
        Assert.Contains(withLog.BuildArguments(), argument => argument.StartsWith("/LOG=", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildArguments_CreatesTheConfiguredLogDirectoryBeforePassingItToSetup()
    {
        var logDirectory = Path.Combine(updatesRoot, "missing", "logs");
        var installer = new SilentUpdateInstaller(updatesRoot, logDirectory: logDirectory);

        var arguments = installer.BuildArguments();

        Assert.True(Directory.Exists(logDirectory));
        Assert.Contains(arguments, argument => argument.StartsWith("/LOG=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_RejectsAnInstallerPathOutsideTheUpdatesRoot()
    {
        // Regression guard: only a file the update service itself placed in
        // the verified updates folder may ever be executed. A path pointing
        // anywhere else must never reach Process.Start.
        var outsidePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(outsidePath, "not from the updates folder");
        try
        {
            var installer = new SilentUpdateInstaller(updatesRoot);
            var update = new DownloadedUpdate(
                StableSemanticVersion.Parse("9.9.9"),
                outsidePath,
                SizeBytes: new FileInfo(outsidePath).Length,
                WasAlreadyDownloaded: false);

            var launch = await installer.StartAsync(update);

            Assert.False(launch.Started);
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task StartAsync_RejectsANonExecutableInstallerPath()
    {
        var path = Path.Combine(updatesRoot, "not-an-installer.txt");
        await File.WriteAllTextAsync(path, "not an installer");
        var installer = new SilentUpdateInstaller(updatesRoot);
        var update = new DownloadedUpdate(
            StableSemanticVersion.Parse("9.9.9"),
            path,
            SizeBytes: new FileInfo(path).Length,
            WasAlreadyDownloaded: false);

        var launch = await installer.StartAsync(update);

        Assert.False(launch.Started);
    }

    [Fact]
    public async Task StartAsync_RejectsAnInstallerThatDoesNotExistOnDisk()
    {
        var installer = new SilentUpdateInstaller(updatesRoot);
        var missingPath = Path.Combine(updatesRoot, "FiveMCleaner-Setup-9.9.9-win-x64.exe");
        var update = new DownloadedUpdate(
            StableSemanticVersion.Parse("9.9.9"),
            missingPath,
            SizeBytes: 1024,
            WasAlreadyDownloaded: false);

        var launch = await installer.StartAsync(update);

        Assert.False(launch.Started);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(42)]
    public void DescribeInnoExitCode_NeverReturnsAnEmptyExplanation(int exitCode)
    {
        var description = SilentUpdateInstaller.DescribeInnoExitCode(exitCode);

        Assert.False(string.IsNullOrWhiteSpace(description));
    }

    [Fact]
    public void Constructor_RejectsARelativeUpdatesRoot()
    {
        Assert.Throws<ArgumentException>(() => new SilentUpdateInstaller("relative\\path"));
    }
}
