using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class GtaVBenchmarkRunnerTests
{
    [Fact]
    public void BuildIterationResult_ComputesAveragesAndLowsFromFrametimes()
    {
        // 100 frames at a steady 16.67ms (~60 FPS) plus 5 stutter frames at
        // 100ms (~10 FPS) to exercise the 1%/0.1% low tail.
        var frametimes = new List<double>();
        for (var i = 0; i < 95; i++)
        {
            frametimes.Add(16.67);
        }

        for (var i = 0; i < 5; i++)
        {
            frametimes.Add(100);
        }

        var result = WindowsGtaVBenchmarkRunner.BuildIterationResult(frametimes);

        Assert.Equal(100, result.SampleCount);
        Assert.True(result.AverageFps is > 50 and < 60);
        Assert.True(result.MinimumFps is > 9 and < 11);
        Assert.True(result.OnePercentLowFps < result.AverageFps);
        Assert.True(result.PeakFrametimeMs >= 100);
    }

    [Fact]
    public void TryParseFrametimeFile_ParsesFrametimeMillisecondsColumn()
    {
        using var temporary = new TemporaryBenchmarkFile(
            "Frame,FrameTime(ms)\n"
            + string.Join("\n", Enumerable.Range(1, 30).Select(i => $"{i},16.6")));

        var result = WindowsGtaVBenchmarkRunner.TryParseFrametimeFile(temporary.Path);

        Assert.NotNull(result);
        Assert.Equal(30, result!.SampleCount);
        Assert.True(result.AverageFps is > 55 and < 65);
    }

    [Fact]
    public void TryParseFrametimeFile_ParsesFpsColumnAsFallback()
    {
        using var temporary = new TemporaryBenchmarkFile(
            "Frame,FPS\n"
            + string.Join("\n", Enumerable.Range(1, 30).Select(i => $"{i},60")));

        var result = WindowsGtaVBenchmarkRunner.TryParseFrametimeFile(temporary.Path);

        Assert.NotNull(result);
        Assert.Equal(30, result!.SampleCount);
        Assert.True(result.AverageFps is > 55 and < 65);
    }

    [Fact]
    public void TryParseFrametimeFile_ParsesSemicolonDelimitedFiles()
    {
        using var temporary = new TemporaryBenchmarkFile(
            "Frame;FrameTime(ms)\n"
            + string.Join("\n", Enumerable.Range(1, 30).Select(i => $"{i};16.6")));

        var result = WindowsGtaVBenchmarkRunner.TryParseFrametimeFile(temporary.Path);

        Assert.NotNull(result);
        Assert.Equal(30, result!.SampleCount);
    }

    [Fact]
    public void TryParseFrametimeFile_ReturnsNullForUnrecognizedFormat()
    {
        using var temporary = new TemporaryBenchmarkFile(
            "Header1,Header2\nsomething,else\nnotanumber,alsoNot");

        var result = WindowsGtaVBenchmarkRunner.TryParseFrametimeFile(temporary.Path);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseFrametimeFile_ReturnsNullWhenTooFewSamplesFound()
    {
        using var temporary = new TemporaryBenchmarkFile("Frame,FrameTime(ms)\n1,16.6\n2,16.7");

        var result = WindowsGtaVBenchmarkRunner.TryParseFrametimeFile(temporary.Path);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseFrametimeFile_ReturnsNullForMissingFile()
    {
        var result = WindowsGtaVBenchmarkRunner.TryParseFrametimeFile(
            Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.csv"));

        Assert.Null(result);
    }

    [Fact]
    public void ComputeMedian_PicksMiddleRunByAverageFpsWithoutMixingRuns()
    {
        var low = new GtaVBenchmarkIterationResult(40, 30, 32, 31, 25, 40, 500);
        var mid = new GtaVBenchmarkIterationResult(60, 45, 48, 46, 16.6, 30, 500);
        var high = new GtaVBenchmarkIterationResult(90, 70, 75, 72, 11, 20, 500);

        var median = WindowsGtaVBenchmarkRunner.ComputeMedian([high, low, mid]);

        Assert.Same(mid, median);
    }

    [Fact]
    public void ComputeMedian_ReturnsNullForEmptyIterations()
    {
        Assert.Null(WindowsGtaVBenchmarkRunner.ComputeMedian([]));
    }

    [Fact]
    public void ComputeMedian_ReturnsTheOnlyIterationWhenThereIsJustOne()
    {
        var only = new GtaVBenchmarkIterationResult(60, 45, 48, 46, 16.6, 30, 500);

        Assert.Same(only, WindowsGtaVBenchmarkRunner.ComputeMedian([only]));
    }

    [Fact]
    public async Task RunAsync_FailsHonestlyWhenExecutableIsMissing()
    {
        var runner = new WindowsGtaVBenchmarkRunner();
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "GTA5.exe");

        var result = await runner.RunAsync(missingPath, 1, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("gta-executable-not-found", result.FailureReason);
        Assert.Empty(result.Iterations);
    }

    [Fact]
    public async Task RunAsync_RejectsInvalidIterationCount()
    {
        var runner = new WindowsGtaVBenchmarkRunner();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync("GTA5.exe", 0, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    private sealed class TemporaryBenchmarkFile : IDisposable
    {
        public TemporaryBenchmarkFile(string content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"fivemcleaner-benchmark-{Guid.NewGuid():N}.csv");
            File.WriteAllText(Path, content);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
