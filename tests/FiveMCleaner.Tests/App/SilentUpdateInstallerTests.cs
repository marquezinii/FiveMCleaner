using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class SilentUpdateInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FiveMCleanerTests", Guid.NewGuid().ToString("N"));
    private readonly string updatesRoot;
    private readonly string updaterSource;
    private readonly string updaterRuntime;

    public SilentUpdateInstallerTests()
    {
        updatesRoot = Path.Combine(root, "Updates");
        updaterRuntime = Path.Combine(root, "Updater");
        updaterSource = Path.Combine(root, "installed", "updater", "FiveMCleaner.Updater.exe");
        Directory.CreateDirectory(updatesRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(updaterSource)!);
        File.WriteAllText(updaterSource, "detached updater");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private DownloadedUpdate CreateVerifiedInstaller(string version = "9.9.9")
    {
        var path = Path.Combine(updatesRoot, $"FiveMCleaner-Setup-{version}-win-x64.exe");
        File.WriteAllText(path, "fake installer bytes");
        return new DownloadedUpdate(StableSemanticVersion.Parse(version), path, new FileInfo(path).Length, new string('a', 64), false);
    }

    private SilentUpdateInstaller Create(IUpdateProcessLauncher? launcher = null, string? logDirectory = null) =>
        new(updatesRoot, logDirectory, updaterSource, updaterRuntime, launcher);

    [Fact]
    public void BuildHandoffArguments_PassesOnlyIntegrityAndLifecycleData()
    {
        var arguments = Create(logDirectory: Path.Combine(root, "logs")).BuildHandoffArguments(
            CreateVerifiedInstaller(), parentProcessId: 42, parentStartTimeUtcFileTime: 123);

        Assert.Contains("--installer", arguments);
        Assert.Contains("--installer-sha256", arguments);
        Assert.Contains("--parent-pid", arguments);
        Assert.Contains("--parent-start-time", arguments);
        Assert.Contains("42", arguments);
        Assert.Contains("--log", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("/VERYSILENT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_CopiesUpdaterOutsideInstallAndStartsIt()
    {
        var launcher = new RecordingLauncher();
        var launch = await Create(launcher).StartAsync(CreateVerifiedInstaller(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(launch.Started);
        Assert.Equal(Path.Combine(updaterRuntime, "FiveMCleaner.Updater.exe"), launcher.UpdaterPath);
        Assert.True(File.Exists(launcher.UpdaterPath));
        Assert.Contains("--installer", launcher.Arguments);
    }

    [Fact]
    public async Task StartAsync_RejectsAnInstallerOutsideTheVerifiedUpdatesRoot()
    {
        var outside = Path.Combine(root, "outside.exe");
        await File.WriteAllTextAsync(outside, "not verified", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        var update = new DownloadedUpdate(StableSemanticVersion.Parse("9.9.9"), outside, 1, new string('a', 64), false);

        var launch = await Create().StartAsync(update, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(launch.Started);
    }

    [Fact]
    public async Task StartAsync_RejectsMissingOrMalformedIntegrityMetadata()
    {
        var update = CreateVerifiedInstaller() with { Sha256Hex = "bad" };

        var launch = await Create().StartAsync(update, cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(launch.Started);
    }

    [Fact]
    public void Constructor_RejectsRelativeDirectories()
    {
        Assert.Throws<ArgumentException>(() => new SilentUpdateInstaller("relative", null, updaterSource, updaterRuntime));
    }

    private sealed class RecordingLauncher : IUpdateProcessLauncher
    {
        public string UpdaterPath { get; private set; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public void Start(string updaterPath, IReadOnlyList<string> arguments)
        {
            UpdaterPath = updaterPath;
            Arguments = arguments;
        }
    }
}
