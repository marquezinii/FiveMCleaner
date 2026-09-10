using System.Windows.Threading;
using Ralven.App.Services;

namespace Ralven.App.ViewModels;

public enum LiveMetricsTarget
{
    System,
    FiveM
}

public enum LiveMetricKind
{
    Cpu,
    Gpu,
    Memory,
    Disk,
    Network
}

public sealed partial class MainViewModel
{
    private CancellationTokenSource? liveMetricsCancellation;
    private bool disposed;

    public double CpuUsagePercent { get => cpuUsagePercent; private set => SetProperty(ref cpuUsagePercent, value); }

    public double GpuUsagePercent { get => gpuUsagePercent; private set => SetProperty(ref gpuUsagePercent, value); }

    public double MemoryUsagePercent { get => memoryUsagePercent; private set => SetProperty(ref memoryUsagePercent, value); }

    public double DiskUsagePercent { get => diskUsagePercent; private set => SetProperty(ref diskUsagePercent, value); }

    public string CpuUsageLabel { get => cpuUsageLabel; private set => SetProperty(ref cpuUsageLabel, value); }

    public string GpuUsageLabel { get => gpuUsageLabel; private set => SetProperty(ref gpuUsageLabel, value); }

    public string MemoryUsageLabel { get => memoryUsageLabel; private set => SetProperty(ref memoryUsageLabel, value); }

    public string DiskUsageLabel { get => diskUsageLabel; private set => SetProperty(ref diskUsageLabel, value); }

    public string NetworkUsageLabel { get => networkUsageLabel; private set => SetProperty(ref networkUsageLabel, value); }

    public string MemoryUsageDetailLabel { get => memoryUsageDetailLabel; private set => SetProperty(ref memoryUsageDetailLabel, value); }

    public string CpuTrendLabel { get => cpuTrendLabel; private set => SetProperty(ref cpuTrendLabel, value); }

    public string GpuTrendLabel { get => gpuTrendLabel; private set => SetProperty(ref gpuTrendLabel, value); }

    public string MemoryTrendLabel { get => memoryTrendLabel; private set => SetProperty(ref memoryTrendLabel, value); }

    public string DiskTrendLabel { get => diskTrendLabel; private set => SetProperty(ref diskTrendLabel, value); }

    public string NetworkTrendLabel { get => networkTrendLabel; private set => SetProperty(ref networkTrendLabel, value); }

    public string LiveMetricsUpdatedLabel { get => liveMetricsUpdatedLabel; private set => SetProperty(ref liveMetricsUpdatedLabel, value); }

    public string LiveMetricsUpdatedExactLabel { get => liveMetricsUpdatedExactLabel; private set => SetProperty(ref liveMetricsUpdatedExactLabel, value); }

    public IReadOnlyList<double> CpuUsageSeries { get => cpuUsageSeries; private set => SetProperty(ref cpuUsageSeries, value); }

    public IReadOnlyList<double> GpuUsageSeries { get => gpuUsageSeries; private set => SetProperty(ref gpuUsageSeries, value); }

    public IReadOnlyList<double> MemoryUsageSeries { get => memoryUsageSeries; private set => SetProperty(ref memoryUsageSeries, value); }

    public IReadOnlyList<double> DiskUsageSeries { get => diskUsageSeries; private set => SetProperty(ref diskUsageSeries, value); }

    public IReadOnlyList<double> NetworkUsageSeries { get => networkUsageSeries; private set => SetProperty(ref networkUsageSeries, value); }

    public bool IsLiveMetricsActive
    {
        get => liveMetricsEnabled;
        private set => SetProperty(ref liveMetricsEnabled, value);
    }

    public bool IsLivePerformanceLive => liveMetricsEnabled
        && lastLiveMetrics is not null
        && !liveMetricsUnavailable
        && !liveMetricsAwaitingFreshSample;

    public bool IsLivePerformanceWaiting => liveMetricsEnabled
        && (lastLiveMetrics is null || liveMetricsAwaitingFreshSample)
        && !liveMetricsUnavailable;

    public bool IsLivePerformanceUnavailable => liveMetricsEnabled && liveMetricsUnavailable;

    public bool IsLivePerformancePaused => !liveMetricsEnabled;

    public bool HasLiveMetricsSample => lastLiveMetrics is not null;

    public bool IsSystemLiveMetricsTarget => liveMetricsTarget == LiveMetricsTarget.System;

    public bool IsFiveMLiveMetricsTarget => liveMetricsTarget == LiveMetricsTarget.FiveM;

    public bool IsFiveMLiveMetricsTargetAvailable => HasLegacySessionRoot();

    public bool IsCpuLiveMetricSelected => selectedLiveMetric == LiveMetricKind.Cpu;

    public bool IsGpuLiveMetricSelected => selectedLiveMetric == LiveMetricKind.Gpu;

    public bool IsMemoryLiveMetricSelected => selectedLiveMetric == LiveMetricKind.Memory;

    public bool IsDiskLiveMetricSelected => selectedLiveMetric == LiveMetricKind.Disk;

    public bool IsNetworkLiveMetricSelected => selectedLiveMetric == LiveMetricKind.Network;

    public bool IsGpuLiveMetricAvailable => liveMetricsTarget == LiveMetricsTarget.System;

    public bool IsDiskLiveMetricAvailable => liveMetricsTarget == LiveMetricsTarget.System;

    public bool IsNetworkLiveMetricAvailable => liveMetricsTarget == LiveMetricsTarget.System;

    public string LiveMetricsTargetDescription => localization.GetString(
        liveMetricsTarget == LiveMetricsTarget.System
            ? "Dashboard.LivePerformance.Description"
            : "Dashboard.LivePerformance.FiveMDescription");

    public IReadOnlyList<double> SelectedLiveMetricSeries => selectedLiveMetric switch
    {
        LiveMetricKind.Cpu => CpuUsageSeries,
        LiveMetricKind.Gpu => GpuUsageSeries,
        LiveMetricKind.Memory => MemoryUsageSeries,
        LiveMetricKind.Disk => DiskUsageSeries,
        LiveMetricKind.Network => NetworkUsageSeries,
        _ => CpuUsageSeries
    };

    public bool HasSelectedLiveMetricSamples => HistoryFor(selectedLiveMetric).Count > 0;

    public string SelectedLiveMetricName => localization.GetString(MetricNameKey(selectedLiveMetric));

    public string SelectedLiveMetricCurrentLabel => CurrentLabelFor(selectedLiveMetric);

    public string SelectedLiveMetricAverageLabel => FormatMetricValue(
        HistoryFor(selectedLiveMetric).Count == 0 ? null : HistoryFor(selectedLiveMetric).Average(),
        selectedLiveMetric);

    public string SelectedLiveMetricPeakLabel => FormatMetricValue(
        HistoryFor(selectedLiveMetric).Count == 0 ? null : HistoryFor(selectedLiveMetric).Max(),
        selectedLiveMetric);

    public double SelectedLiveMetricMaximum => UsesPercentageScale(selectedLiveMetric)
        ? 100
        : NiceMaximum(HistoryFor(selectedLiveMetric));

    public double MemoryChartMaximum => liveMetricsTarget == LiveMetricsTarget.FiveM
        ? NiceMaximum(memoryUsageHistory)
        : 100;

    public double NetworkChartMaximum => NiceMaximum(networkUsageHistory);

    public string SelectedLiveMetricValueFormat => UsesPercentageScale(selectedLiveMetric) ? "0" : "0.0";

    public string SelectedLiveMetricValueSuffix => selectedLiveMetric switch
    {
        LiveMetricKind.Network => " MB/s",
        LiveMetricKind.Memory when liveMetricsTarget == LiveMetricsTarget.FiveM => " GB",
        _ => "%"
    };

    public string SelectedLiveMetricEmptyLabel
    {
        get
        {
            if (liveMetricsTarget == LiveMetricsTarget.FiveM && lastLiveMetrics?.FiveMProcessCount == 0)
            {
                return localization.GetString("Dashboard.LivePerformance.Status.FiveMNotRunning");
            }

            if (liveMetricsTarget == LiveMetricsTarget.FiveM
                && lastLiveMetrics?.FiveMProcessCount is > 0
                && selectedLiveMetric == LiveMetricKind.Cpu)
            {
                return localization.GetString("Dashboard.LivePerformance.Status.Collecting");
            }

            return localization.GetString("Dashboard.LivePerformance.Status.MetricUnavailable");
        }
    }

    public string SelectedLiveMetricStatusLabel
    {
        get
        {
            if (lastLiveMetrics is null)
            {
                return localization.GetString("Dashboard.LivePerformance.Status.Collecting");
            }

            if (liveMetricsTarget == LiveMetricsTarget.FiveM)
            {
                if (lastLiveMetrics.FiveMProcessCount is null)
                {
                    return localization.GetString("Dashboard.LivePerformance.Status.MetricUnavailable");
                }

                if (lastLiveMetrics.FiveMProcessCount == 0)
                {
                    return localization.GetString("Dashboard.LivePerformance.Status.FiveMNotRunning");
                }

                return localization.GetString("Dashboard.LivePerformance.Status.FiveMReadOnly");
            }

            var history = HistoryFor(selectedLiveMetric);
            if (history.Count == 0)
            {
                return localization.GetString("Dashboard.LivePerformance.Status.MetricUnavailable");
            }

            if (selectedLiveMetric == LiveMetricKind.Network)
            {
                return localization.GetString("Dashboard.LivePerformance.Status.Network");
            }

            if (history.Count < 5)
            {
                return localization.GetString("Dashboard.LivePerformance.Status.Collecting");
            }

            var threshold = selectedLiveMetric == LiveMetricKind.Memory ? 90 : 85;
            return history.TakeLast(5).Average() >= threshold
                ? localization.GetString("Dashboard.LivePerformance.Status.SustainedHigh")
                : localization.GetString("Dashboard.LivePerformance.Status.NoSustainedHigh");
        }
    }

    public string SelectedLiveMetricStatusBrushKey =>
        liveMetricsTarget == LiveMetricsTarget.System
        && selectedLiveMetric != LiveMetricKind.Network
        && HistoryFor(selectedLiveMetric).Count >= 5
        && HistoryFor(selectedLiveMetric).TakeLast(5).Average()
            >= (selectedLiveMetric == LiveMetricKind.Memory ? 90 : 85)
            ? "WarningBaseBrush"
            : "TextSecondaryBrush";

    public string SelectedLiveMetricAutomationLabel => localization.Format(
        "Dashboard.LivePerformance.ChartAutomation",
        SelectedLiveMetricName,
        SelectedLiveMetricCurrentLabel,
        SelectedLiveMetricAverageLabel,
        SelectedLiveMetricPeakLabel);

    public void SetLiveMetricsEnabled(bool enabled)
    {
        if (disposed || liveMetricsEnabled == enabled)
        {
            return;
        }

        IsLiveMetricsActive = enabled;
        liveMetricsAwaitingFreshSample = true;
        if (lastLiveMetrics is null)
        {
            ResetLiveMetricPresentation();
        }
        else
        {
            LiveMetricsUpdatedLabel = localization.GetString(
                enabled
                    ? "Dashboard.LivePerformance.Waiting"
                    : "Dashboard.LivePerformance.PausedDetail");
        }
        NotifyLivePerformanceStateChanged();
        NotifySelectedLiveMetricValuesChanged();
        if (!enabled)
        {
            liveMetricsTimer?.Stop();
            liveMetricsCancellation?.Cancel();
            return;
        }

        RefreshGreeting();
        RefreshFiveMSessionMonitorPresentation();

        liveMetricsTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = LiveMetricsInterval
        };
        liveMetricsTimer.Tick -= LiveMetricsTimer_Tick;
        liveMetricsTimer.Tick += LiveMetricsTimer_Tick;
        liveMetricsTimer.Start();
        _ = CaptureLiveMetricsAsync();
    }

    public void SelectLiveMetricsTarget(LiveMetricsTarget target)
    {
        if (target == liveMetricsTarget
            || (target == LiveMetricsTarget.FiveM && !IsFiveMLiveMetricsTargetAvailable))
        {
            return;
        }

        liveMetricsTarget = target;
        selectedLiveMetric = LiveMetricKind.Cpu;
        liveMetricsGeneration++;
        liveMetricsUnavailable = false;
        liveMetricsAwaitingFreshSample = liveMetricsEnabled;
        lastLiveMetrics = null;
        ClearLiveMetricHistory();
        ResetLiveMetricPresentation();
        liveMetricsCancellation?.Cancel();
        NotifyLivePerformanceStateChanged();
        NotifyLiveMetricSelectionChanged();
        if (liveMetricsEnabled && !liveMetricsCaptureInProgress)
        {
            _ = CaptureLiveMetricsAsync();
        }
    }

    public void SelectLiveMetric(LiveMetricKind metric)
    {
        if (metric == selectedLiveMetric
            || (liveMetricsTarget == LiveMetricsTarget.FiveM
                && metric is not (LiveMetricKind.Cpu or LiveMetricKind.Memory)))
        {
            return;
        }

        selectedLiveMetric = metric;
        NotifyLiveMetricSelectionChanged();
    }

    private void LiveMetricsTimer_Tick(object? sender, EventArgs e) => _ = CaptureLiveMetricsAsync();

    private async Task CaptureLiveMetricsAsync()
    {
        if (!liveMetricsEnabled || liveMetricsCaptureInProgress || isPersonalBusy)
        {
            return;
        }

        liveMetricsCaptureInProgress = true;
        var generation = liveMetricsGeneration;
        var target = liveMetricsTarget;
        using var cancellation = new CancellationTokenSource();
        liveMetricsCancellation = cancellation;
        try
        {
            var snapshot = target == LiveMetricsTarget.System
                ? await liveSystemMetricsProvider.CaptureAsync(cancellation.Token)
                : await liveSystemMetricsProvider.CaptureFiveMAsync(
                    diagnostic?.FiveMRoot ?? string.Empty,
                    cancellation.Token);
            if (!liveMetricsEnabled
                || cancellation.IsCancellationRequested
                || generation != liveMetricsGeneration)
            {
                return;
            }

            liveMetricsUnavailable = target == LiveMetricsTarget.FiveM
                && snapshot.FiveMProcessCount is null;
            liveMetricsAwaitingFreshSample = false;
            ApplyLiveMetrics(snapshot);
            lastLiveMetrics = snapshot;
            NotifyLivePerformanceStateChanged();
            NotifySelectedLiveMetricValuesChanged();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Hiding the surface or changing its target is a normal pause.
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (liveMetricsEnabled
                && !cancellation.IsCancellationRequested
                && generation == liveMetricsGeneration)
            {
                liveMetricsUnavailable = true;
                liveMetricsAwaitingFreshSample = false;
                LiveMetricsUpdatedLabel = localization.GetString("Dashboard.LivePerformance.Unavailable");
                NotifyLivePerformanceStateChanged();
                NotifySelectedLiveMetricValuesChanged();
            }
        }
        finally
        {
            liveMetricsCaptureInProgress = false;
            liveMetricsCancellation = null;
            if ((cancellation.IsCancellationRequested || generation != liveMetricsGeneration)
                && liveMetricsEnabled
                && !disposed)
            {
                _ = CaptureLiveMetricsAsync();
            }
        }
    }

    private void ApplyLiveMetrics(LiveSystemMetricsSnapshot snapshot, bool addHistory = true)
    {
        if (liveMetricsTarget == LiveMetricsTarget.System)
        {
            ApplySystemLiveMetrics(snapshot, addHistory);
        }
        else
        {
            ApplyFiveMLiveMetrics(snapshot, addHistory);
        }

        if (addHistory)
        {
            CpuUsageSeries = cpuUsageHistory.ToArray();
            GpuUsageSeries = gpuUsageHistory.ToArray();
            MemoryUsageSeries = memoryUsageHistory.ToArray();
            DiskUsageSeries = diskUsageHistory.ToArray();
            NetworkUsageSeries = networkUsageHistory.ToArray();
        }

        CpuTrendLabel = DescribeTrend(cpuUsageHistory, LiveMetricKind.Cpu);
        GpuTrendLabel = DescribeTrend(gpuUsageHistory, LiveMetricKind.Gpu);
        MemoryTrendLabel = DescribeTrend(memoryUsageHistory, LiveMetricKind.Memory);
        DiskTrendLabel = DescribeTrend(diskUsageHistory, LiveMetricKind.Disk);
        NetworkTrendLabel = DescribeTrend(networkUsageHistory, LiveMetricKind.Network);
        LiveMetricsUpdatedLabel = localization.GetString(
            !liveMetricsEnabled
                ? "Dashboard.LivePerformance.PausedDetail"
                : liveMetricsAwaitingFreshSample
                    ? "Dashboard.LivePerformance.Waiting"
                    : "Dashboard.LivePerformance.UpdatedNow");
        LiveMetricsUpdatedExactLabel = localization.Format(
            "Dashboard.LivePerformance.Updated",
            snapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss"));
    }

    private void ApplySystemLiveMetrics(LiveSystemMetricsSnapshot snapshot, bool addHistory)
    {
        CpuUsagePercent = snapshot.CpuPercent ?? 0;
        GpuUsagePercent = snapshot.GpuPercent ?? 0;
        MemoryUsagePercent = snapshot.MemoryPercent ?? 0;
        DiskUsagePercent = snapshot.DiskPercent ?? 0;
        CpuUsageLabel = FormatLivePercent(snapshot.CpuPercent);
        GpuUsageLabel = FormatLivePercent(snapshot.GpuPercent);
        MemoryUsageLabel = FormatLivePercent(snapshot.MemoryPercent);
        DiskUsageLabel = FormatLivePercent(snapshot.DiskPercent);
        NetworkUsageLabel = FormatNetwork(snapshot.NetworkThroughputMBps);
        MemoryUsageDetailLabel = snapshot is { UsedMemoryGiB: { } used, TotalMemoryGiB: { } total }
            ? localization.Format("Dashboard.LivePerformance.MemoryDetail", used, total)
            : string.Empty;

        if (!addHistory)
        {
            return;
        }

        AddMetricSample(cpuUsageHistory, snapshot.CpuPercent, clampPercentage: true);
        AddMetricSample(gpuUsageHistory, snapshot.GpuPercent, clampPercentage: true);
        AddMetricSample(memoryUsageHistory, snapshot.MemoryPercent, clampPercentage: true);
        AddMetricSample(diskUsageHistory, snapshot.DiskPercent, clampPercentage: true);
        AddMetricSample(networkUsageHistory, snapshot.NetworkThroughputMBps, clampPercentage: false);
    }

    private void ApplyFiveMLiveMetrics(LiveSystemMetricsSnapshot snapshot, bool addHistory)
    {
        CpuUsagePercent = snapshot.CpuPercent ?? 0;
        GpuUsagePercent = 0;
        MemoryUsagePercent = 0;
        DiskUsagePercent = 0;
        CpuUsageLabel = FormatLivePercent(snapshot.CpuPercent);
        GpuUsageLabel = localization.GetString("Dashboard.LivePerformance.NotAvailable");
        MemoryUsageLabel = FormatGigabytes(snapshot.UsedMemoryGiB);
        DiskUsageLabel = localization.GetString("Dashboard.LivePerformance.NotAvailable");
        NetworkUsageLabel = localization.GetString("Dashboard.LivePerformance.NotAvailable");
        MemoryUsageDetailLabel = snapshot.FiveMProcessCount is > 0
            ? localization.Format("Dashboard.LivePerformance.FiveMProcessCount", snapshot.FiveMProcessCount.Value)
            : string.Empty;

        if (!addHistory)
        {
            return;
        }

        if (snapshot.FiveMProcessCount is not > 0)
        {
            cpuUsageHistory.Clear();
            memoryUsageHistory.Clear();
            return;
        }

        if (snapshot.CpuPercent is null)
        {
            cpuUsageHistory.Clear();
        }

        AddMetricSample(cpuUsageHistory, snapshot.CpuPercent, clampPercentage: true);
        AddMetricSample(memoryUsageHistory, snapshot.UsedMemoryGiB, clampPercentage: false);
    }

    private void ResetLiveMetricPresentation()
    {
        var waiting = localization.GetString("Dashboard.LivePerformance.Waiting");
        CpuUsageLabel = waiting;
        GpuUsageLabel = waiting;
        MemoryUsageLabel = waiting;
        DiskUsageLabel = waiting;
        NetworkUsageLabel = waiting;
        MemoryUsageDetailLabel = string.Empty;
        LiveMetricsUpdatedLabel = localization.GetString(
            liveMetricsEnabled
                ? "Dashboard.LivePerformance.Waiting"
                : "Dashboard.LivePerformance.PausedDetail");
        LiveMetricsUpdatedExactLabel = string.Empty;
        var trend = liveMetricsEnabled
            ? waiting
            : localization.GetString("Dashboard.LivePerformance.NotAvailable");
        CpuTrendLabel = trend;
        GpuTrendLabel = trend;
        MemoryTrendLabel = trend;
        DiskTrendLabel = trend;
        NetworkTrendLabel = trend;
    }

    private void ClearLiveMetricHistory()
    {
        cpuUsageHistory.Clear();
        gpuUsageHistory.Clear();
        memoryUsageHistory.Clear();
        diskUsageHistory.Clear();
        networkUsageHistory.Clear();
        CpuUsageSeries = [];
        GpuUsageSeries = [];
        MemoryUsageSeries = [];
        DiskUsageSeries = [];
        NetworkUsageSeries = [];
    }

    private void NotifyLivePerformanceStateChanged()
    {
        OnPropertyChanged(nameof(IsLivePerformanceLive));
        OnPropertyChanged(nameof(IsLivePerformanceWaiting));
        OnPropertyChanged(nameof(IsLivePerformanceUnavailable));
        OnPropertyChanged(nameof(IsLivePerformancePaused));
        OnPropertyChanged(nameof(HasLiveMetricsSample));
    }

    private void NotifyLiveMetricSelectionChanged()
    {
        OnPropertyChanged(nameof(IsSystemLiveMetricsTarget));
        OnPropertyChanged(nameof(IsFiveMLiveMetricsTarget));
        OnPropertyChanged(nameof(IsFiveMLiveMetricsTargetAvailable));
        OnPropertyChanged(nameof(IsCpuLiveMetricSelected));
        OnPropertyChanged(nameof(IsGpuLiveMetricSelected));
        OnPropertyChanged(nameof(IsMemoryLiveMetricSelected));
        OnPropertyChanged(nameof(IsDiskLiveMetricSelected));
        OnPropertyChanged(nameof(IsNetworkLiveMetricSelected));
        OnPropertyChanged(nameof(IsGpuLiveMetricAvailable));
        OnPropertyChanged(nameof(IsDiskLiveMetricAvailable));
        OnPropertyChanged(nameof(IsNetworkLiveMetricAvailable));
        OnPropertyChanged(nameof(LiveMetricsTargetDescription));
        NotifySelectedLiveMetricValuesChanged();
    }

    private void NotifySelectedLiveMetricValuesChanged()
    {
        OnPropertyChanged(nameof(SelectedLiveMetricSeries));
        OnPropertyChanged(nameof(HasSelectedLiveMetricSamples));
        OnPropertyChanged(nameof(SelectedLiveMetricName));
        OnPropertyChanged(nameof(SelectedLiveMetricCurrentLabel));
        OnPropertyChanged(nameof(SelectedLiveMetricAverageLabel));
        OnPropertyChanged(nameof(SelectedLiveMetricPeakLabel));
        OnPropertyChanged(nameof(SelectedLiveMetricMaximum));
        OnPropertyChanged(nameof(MemoryChartMaximum));
        OnPropertyChanged(nameof(NetworkChartMaximum));
        OnPropertyChanged(nameof(SelectedLiveMetricValueFormat));
        OnPropertyChanged(nameof(SelectedLiveMetricValueSuffix));
        OnPropertyChanged(nameof(SelectedLiveMetricEmptyLabel));
        OnPropertyChanged(nameof(SelectedLiveMetricStatusLabel));
        OnPropertyChanged(nameof(SelectedLiveMetricStatusBrushKey));
        OnPropertyChanged(nameof(SelectedLiveMetricAutomationLabel));
    }

    private Queue<double> HistoryFor(LiveMetricKind metric) => metric switch
    {
        LiveMetricKind.Cpu => cpuUsageHistory,
        LiveMetricKind.Gpu => gpuUsageHistory,
        LiveMetricKind.Memory => memoryUsageHistory,
        LiveMetricKind.Disk => diskUsageHistory,
        LiveMetricKind.Network => networkUsageHistory,
        _ => cpuUsageHistory
    };

    private string CurrentLabelFor(LiveMetricKind metric) => metric switch
    {
        LiveMetricKind.Cpu => CpuUsageLabel,
        LiveMetricKind.Gpu => GpuUsageLabel,
        LiveMetricKind.Memory => MemoryUsageLabel,
        LiveMetricKind.Disk => DiskUsageLabel,
        LiveMetricKind.Network => NetworkUsageLabel,
        _ => CpuUsageLabel
    };

    private string DescribeTrend(Queue<double> history, LiveMetricKind metric)
    {
        if (history.Count == 0)
        {
            return localization.GetString("Dashboard.LivePerformance.NotAvailable");
        }

        var key = metric switch
        {
            LiveMetricKind.Network => "Dashboard.LivePerformance.TrendValueNetwork",
            LiveMetricKind.Memory when liveMetricsTarget == LiveMetricsTarget.FiveM
                => "Dashboard.LivePerformance.TrendValueGigabytes",
            _ => "Dashboard.LivePerformance.TrendValue"
        };
        return localization.Format(key, history.Average(), history.Max());
    }

    private string FormatMetricValue(double? value, LiveMetricKind metric) => metric switch
    {
        LiveMetricKind.Network => FormatNetwork(value),
        LiveMetricKind.Memory when liveMetricsTarget == LiveMetricsTarget.FiveM
            => FormatGigabytes(value),
        _ => FormatLivePercent(value)
    };

    private string FormatLivePercent(double? value) => value is { } available
        ? localization.Format("Dashboard.LivePerformance.PercentValue", available)
        : localization.GetString("Dashboard.LivePerformance.NotAvailable");

    private string FormatNetwork(double? value) => value is { } available
        ? localization.Format("Dashboard.LivePerformance.NetworkValue", available)
        : localization.GetString("Dashboard.LivePerformance.NotAvailable");

    private string FormatGigabytes(double? value) => value is { } available
        ? localization.Format("Dashboard.LivePerformance.GigabytesValue", available)
        : localization.GetString("Dashboard.LivePerformance.NotAvailable");

    private bool UsesPercentageScale(LiveMetricKind metric) => metric != LiveMetricKind.Network
        && !(liveMetricsTarget == LiveMetricsTarget.FiveM && metric == LiveMetricKind.Memory);

    private static string MetricNameKey(LiveMetricKind metric) => metric switch
    {
        LiveMetricKind.Cpu => "Dashboard.LivePerformance.Cpu",
        LiveMetricKind.Gpu => "Dashboard.LivePerformance.Gpu",
        LiveMetricKind.Memory => "Dashboard.LivePerformance.Memory",
        LiveMetricKind.Disk => "Dashboard.LivePerformance.Disk",
        LiveMetricKind.Network => "Dashboard.LivePerformance.Network",
        _ => "Dashboard.LivePerformance.Cpu"
    };

    internal static double NiceMaximum(IEnumerable<double> values)
    {
        var maximum = values.DefaultIfEmpty(0).Max();
        if (!double.IsFinite(maximum) || maximum <= 1)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(maximum)));
        var normalized = maximum / magnitude;
        var multiplier = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return multiplier * magnitude;
    }

    private static void AddMetricSample(
        Queue<double> history,
        double? value,
        bool clampPercentage)
    {
        if (value is null || !double.IsFinite(value.Value))
        {
            return;
        }

        history.Enqueue(clampPercentage
            ? Math.Clamp(value.Value, 0, 100)
            : Math.Max(0, value.Value));
        while (history.Count > LiveMetricsHistoryCapacity)
        {
            history.Dequeue();
        }
    }
}
