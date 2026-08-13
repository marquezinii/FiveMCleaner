using System.Diagnostics;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class ProcessCommandRunnerTests
{
    private static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task RunAsync_ReturnsOutputAndExitCodeForACommandThatCompletes()
    {
        var runner = new ProcessCommandRunner();

        var result = await runner.RunAsync(
            CmdPath,
            ["/c", "echo fivemcleaner"],
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("fivemcleaner", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_KillsAndReportsATimeoutWhenTheProcessNeverExits()
    {
        var runner = new ProcessCommandRunner();

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            CmdPath,
            ["/c", "ping -n 30 127.0.0.1"],
            TimeSpan.FromSeconds(2),
            CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_TimesOutInsteadOfHangingWhenAGrandchildInheritsTheOutputPipe()
    {
        // Regression guard: the launched cmd exits almost immediately, but the
        // detached grandchild keeps the inherited stdout handle open for ~30s.
        // The output read used to be awaited with no timeout at all, so this
        // call blocked until the grandchild died, long past the caller's
        // timeout. The read is now bounded by the same token as the wait.
        var runner = new ProcessCommandRunner();
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            CmdPath,
            ["/c", "start /b ping -n 30 127.0.0.1"],
            TimeSpan.FromSeconds(2),
            CancellationToken.None));

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"The call should have honored its 2s timeout, but took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task RunAsync_PropagatesCallerCancellationRatherThanATimeout()
    {
        var runner = new ProcessCommandRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            CmdPath,
            ["/c", "ping -n 30 127.0.0.1"],
            TimeSpan.FromMinutes(5),
            cancellation.Token));
    }

    [Fact]
    public async Task RunAsync_RejectsExecutablesThatAreNotFullyQualified()
    {
        var runner = new ProcessCommandRunner();

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(
            "cmd.exe",
            [],
            TimeSpan.FromSeconds(5),
            CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_RejectsMissingExecutable()
    {
        var runner = new ProcessCommandRunner();
        var missingExecutable = Path.Combine(
            Path.GetTempPath(),
            $"FiveMCleaner-{Guid.NewGuid():N}.exe");

        await Assert.ThrowsAsync<FileNotFoundException>(() => runner.RunAsync(
            missingExecutable,
            [],
            TimeSpan.FromSeconds(5),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RunAsync_RejectsNonPositiveTimeout(int timeoutSeconds)
    {
        var runner = new ProcessCommandRunner();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            CmdPath,
            [],
            TimeSpan.FromSeconds(timeoutSeconds),
            CancellationToken.None));
    }
}
