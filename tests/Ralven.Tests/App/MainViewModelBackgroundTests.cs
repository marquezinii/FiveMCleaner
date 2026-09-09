using System.Reflection;
using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.App;

public sealed class MainViewModelBackgroundTests
{
    [Fact]
    public async Task CancelledProvider_DoesNotReportAnUnavailableReading()
    {
        var provider = new ControlledMetricsProvider();
        using var viewModel = Create(provider);
        viewModel.SetLiveMetricsEnabled(true);
        viewModel.SetLiveMetricsEnabled(false);
        provider.Cancel(0);
        await WaitForCaptureToFinish(viewModel);

        Assert.False(viewModel.IsLivePerformanceUnavailable);
        viewModel.SetLiveMetricsEnabled(true);
        provider.Complete(1, 37);
        await WaitFor(() => viewModel.HasLiveMetricsSample);
        Assert.True(viewModel.IsLivePerformanceLive);
    }

    [Fact]
    public async Task RepeatedActivation_DoesNotStartAnotherCapture()
    {
        var provider = new ControlledMetricsProvider();
        using var viewModel = Create(provider);
        viewModel.SetLiveMetricsEnabled(true);
        provider.Complete(0, 12);
        await WaitFor(() => viewModel.HasLiveMetricsSample);

        viewModel.SetLiveMetricsEnabled(true);

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Hide_CancelsCaptureAndDiscardsLateSample()
    {
        var provider = new ControlledMetricsProvider();
        using var viewModel = Create(provider);
        viewModel.SetLiveMetricsEnabled(true);

        viewModel.SetLiveMetricsEnabled(false);
        Assert.True(provider.Token(0).IsCancellationRequested);
        provider.Complete(0, 12);
        await WaitForCaptureToFinish(viewModel);

        Assert.False(viewModel.HasLiveMetricsSample);
        Assert.False(viewModel.IsLiveMetricsActive);
        Assert.False(viewModel.IsLivePerformanceUnavailable);
    }

    [Fact]
    public async Task RapidRestore_WaitsForCancelledCaptureThenTakesFreshSample()
    {
        var provider = new ControlledMetricsProvider();
        using var viewModel = Create(provider);
        viewModel.SetLiveMetricsEnabled(true);
        viewModel.SetLiveMetricsEnabled(false);
        viewModel.SetLiveMetricsEnabled(true);
        Assert.Equal(1, provider.Calls);

        provider.Complete(0, 12);
        await WaitFor(() => provider.Calls == 2);
        Assert.False(viewModel.HasLiveMetricsSample);
        provider.Complete(1, 37);
        await WaitFor(() => viewModel.HasLiveMetricsSample);

        Assert.Equal([37d], viewModel.CpuUsageSeries);
        Assert.False(provider.Token(1).IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_CancelsCaptureAndPreventsReactivation()
    {
        var provider = new ControlledMetricsProvider();
        var viewModel = Create(provider);
        viewModel.SetLiveMetricsEnabled(true);

        viewModel.Dispose();
        viewModel.SetLiveMetricsEnabled(true);
        Assert.True(provider.Token(0).IsCancellationRequested);
        provider.Complete(0, 12);
        await WaitForCaptureToFinish(viewModel);

        Assert.Equal(1, provider.Calls);
        Assert.False(viewModel.IsLiveMetricsActive);
        Assert.False(viewModel.HasLiveMetricsSample);
    }

    [Fact]
    public async Task HiddenSessionMonitor_KeepsStateAndAvoidsUnchangedPresentationWork()
    {
        var presence = FiveMSessionPresence.Present;
        using var viewModel = Create(new ControlledMetricsProvider(), _ => presence);
        await viewModel.InitializeAsync();
        viewModel.ToggleFiveMSessionMonitor();
        await WaitFor(() => viewModel.IsFiveMSessionActive);
        await WaitForSessionProbeToFinish(viewModel);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        await Probe(viewModel);

        Assert.True(viewModel.IsFiveMSessionMonitoring);
        Assert.True(viewModel.IsFiveMSessionActive);
        Assert.Empty(notifications);

        presence = FiveMSessionPresence.AbsentConfirmed;
        await Probe(viewModel);
        Assert.True(viewModel.IsFiveMSessionEndConfirmationPending);
        Assert.True(viewModel.IsFiveMSessionActive);
        await Probe(viewModel);
        Assert.False(viewModel.IsFiveMSessionActive);
        Assert.Contains(nameof(MainViewModel.CanStart), notifications);
    }

    [Fact]
    public async Task RestartSessionMonitor_DiscardsResultFromPreviousRun()
    {
        using var release = new ManualResetEventSlim();
        var calls = 0;
        using var viewModel = Create(new ControlledMetricsProvider(), _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                return FiveMSessionPresence.Present;
            }

            return FiveMSessionPresence.AbsentConfirmed;
        });
        await viewModel.InitializeAsync();
        var becameActive = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsFiveMSessionActive))
                becameActive |= viewModel.IsFiveMSessionActive;
        };
        try
        {
            viewModel.ToggleFiveMSessionMonitor();
            await WaitFor(() => Volatile.Read(ref calls) == 1);
            viewModel.ToggleFiveMSessionMonitor();
            viewModel.ToggleFiveMSessionMonitor();
        }
        finally
        {
            release.Set();
        }

        await WaitFor(() => Volatile.Read(ref calls) == 2);
        await WaitForSessionProbeToFinish(viewModel);
        Assert.True(viewModel.IsFiveMSessionMonitoring);
        Assert.False(becameActive);
        Assert.False(viewModel.IsFiveMSessionActive);
    }

    private static MainViewModel Create(ILiveSystemMetricsProvider provider, Func<string, FiveMSessionPresence>? probe = null) => new(
        new FakeAppOptimizationService(new AppSettings(), settingsFileExists: false, fiveMRoot: @"C:\FiveM"),
        liveSystemMetricsProvider: provider,
        fiveMSessionProbe: probe);

    private static Task Probe(MainViewModel viewModel) => (Task)typeof(MainViewModel)
        .GetMethod("ProbeFiveMSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewModel, null)!;

    private static Task WaitForCaptureToFinish(MainViewModel viewModel) => WaitFor(() =>
        typeof(MainViewModel).GetField("liveMetricsCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel) is null);

    private static Task WaitForSessionProbeToFinish(MainViewModel viewModel) => WaitFor(() =>
        !(bool)typeof(MainViewModel).GetField("fiveMSessionProbeInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!);

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledMetricsProvider : ILiveSystemMetricsProvider
    {
        private readonly List<(TaskCompletionSource<LiveSystemMetricsSnapshot> Completion, CancellationToken Token)> captures = [];
        public int Calls { get { lock (captures) return captures.Count; } }

        public Task<LiveSystemMetricsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<LiveSystemMetricsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (captures) captures.Add((completion, cancellationToken));
            return completion.Task;
        }

        public CancellationToken Token(int index) { lock (captures) return captures[index].Token; }

        public void Cancel(int index)
        {
            lock (captures) captures[index].Completion.SetCanceled(captures[index].Token);
        }

        public void Complete(int index, double cpu)
        {
            lock (captures) captures[index].Completion.SetResult(new(cpu, 10, 50, 10, 0, DateTimeOffset.UtcNow));
        }
    }
}
