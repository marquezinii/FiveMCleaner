using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows.Threading;
using Ralven.App.Services;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Core.Planning;

namespace Ralven.App.ViewModels;

public sealed partial class MainViewModel
{
    public double CpuUsagePercent { get => cpuUsagePercent; private set => SetProperty(ref cpuUsagePercent, value); }

    public double GpuUsagePercent { get => gpuUsagePercent; private set => SetProperty(ref gpuUsagePercent, value); }

    public double MemoryUsagePercent { get => memoryUsagePercent; private set => SetProperty(ref memoryUsagePercent, value); }

    public double DiskUsagePercent { get => diskUsagePercent; private set => SetProperty(ref diskUsagePercent, value); }

    public string CpuUsageLabel { get => cpuUsageLabel; private set => SetProperty(ref cpuUsageLabel, value); }

    public string GpuUsageLabel { get => gpuUsageLabel; private set => SetProperty(ref gpuUsageLabel, value); }

    public string MemoryUsageLabel { get => memoryUsageLabel; private set => SetProperty(ref memoryUsageLabel, value); }

    public string DiskUsageLabel { get => diskUsageLabel; private set => SetProperty(ref diskUsageLabel, value); }

    public string NetworkUsageLabel { get => networkUsageLabel; private set => SetProperty(ref networkUsageLabel, value); }

    /// <summary>Live memory reading in absolute terms (e.g. "12,4 / 31,9 GB").</summary>
    public string MemoryUsageDetailLabel { get => memoryUsageDetailLabel; private set => SetProperty(ref memoryUsageDetailLabel, value); }

    /// <summary>Average and peak CPU over the samples currently plotted.</summary>
    public string CpuTrendLabel { get => cpuTrendLabel; private set => SetProperty(ref cpuTrendLabel, value); }

    /// <summary>Average and peak GPU over the samples currently plotted.</summary>
    public string GpuTrendLabel { get => gpuTrendLabel; private set => SetProperty(ref gpuTrendLabel, value); }

    public string LiveMetricsUpdatedLabel { get => liveMetricsUpdatedLabel; private set => SetProperty(ref liveMetricsUpdatedLabel, value); }

    /// <summary>
    /// Histórico de CPU em porcentagem, da amostra mais antiga para a mais
    /// recente. A cena 3D da Visão geral consome os valores crus e cuida da
    /// projeção; o modelo não conhece geometria de tela.
    /// </summary>
    public IReadOnlyList<double> CpuUsageSeries { get => cpuUsageSeries; private set => SetProperty(ref cpuUsageSeries, value); }

    /// <summary>Histórico de GPU em porcentagem, na mesma ordem de <see cref="CpuUsageSeries"/>.</summary>
    public IReadOnlyList<double> GpuUsageSeries { get => gpuUsageSeries; private set => SetProperty(ref gpuUsageSeries, value); }

    /// <summary>
    /// Verdadeiro enquanto a Visão geral está ativa e coletando. A cena 3D usa
    /// esse estado para parar de animar quando a página sai de cena ou a janela
    /// vai para a bandeja, em vez de girar sem ninguém olhando.
    /// </summary>
    public bool IsLiveMetricsActive
    {
        get => liveMetricsEnabled;
        private set => SetProperty(ref liveMetricsEnabled, value);
    }

    /// <summary>
    /// Estado real do bloco "Desempenho ao vivo": a pílula, o gráfico e o
    /// rótulo de atualização precisam concordar entre si em vez de a pílula
    /// dizer "AO VIVO" enquanto os valores ainda leem "Lendo..." ou falharam.
    /// </summary>
    public bool IsLivePerformanceLive => liveMetricsEnabled && lastLiveMetrics is not null && !liveMetricsUnavailable;

    public bool IsLivePerformanceWaiting => liveMetricsEnabled && lastLiveMetrics is null && !liveMetricsUnavailable;

    public bool IsLivePerformanceUnavailable => liveMetricsEnabled && liveMetricsUnavailable;

    public bool HasLiveMetricsSample => lastLiveMetrics is not null;

    private void NotifyLivePerformanceStateChanged()
    {
        OnPropertyChanged(nameof(IsLivePerformanceLive));
        OnPropertyChanged(nameof(IsLivePerformanceWaiting));
        OnPropertyChanged(nameof(IsLivePerformanceUnavailable));
        OnPropertyChanged(nameof(HasLiveMetricsSample));
    }

    public void SetLiveMetricsEnabled(bool enabled)
    {
        IsLiveMetricsActive = enabled;
        NotifyLivePerformanceStateChanged();
        if (!enabled)
        {
            liveMetricsTimer?.Stop();
            return;
        }

        // A saudação só é recalculada aqui (ao reabrir a Visão geral), não em
        // um timer próprio: ela muda no máximo três vezes por dia, então não
        // vale a pena um relógio dedicado para isso.
        RefreshGreeting();

        liveMetricsTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = LiveMetricsInterval
        };
        liveMetricsTimer.Tick -= LiveMetricsTimer_Tick;
        liveMetricsTimer.Tick += LiveMetricsTimer_Tick;
        liveMetricsTimer.Start();
        _ = CaptureLiveMetricsAsync();
    }

    private void LiveMetricsTimer_Tick(object? sender, EventArgs e) => _ = CaptureLiveMetricsAsync();

    private async Task CaptureLiveMetricsAsync()
    {
        if (!liveMetricsEnabled || liveMetricsCaptureInProgress || isPersonalBusy)
        {
            return;
        }

        liveMetricsCaptureInProgress = true;
        try
        {
            var snapshot = await liveSystemMetricsProvider.CaptureAsync();
            if (!liveMetricsEnabled)
            {
                return;
            }

            lastLiveMetrics = snapshot;
            liveMetricsUnavailable = false;
            ApplyLiveMetrics(snapshot);
            NotifyLivePerformanceStateChanged();
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (liveMetricsEnabled)
            {
                liveMetricsUnavailable = true;
                LiveMetricsUpdatedLabel = localization.GetString("Dashboard.LivePerformance.Unavailable");
                NotifyLivePerformanceStateChanged();
            }
        }
        finally
        {
            liveMetricsCaptureInProgress = false;
        }
    }

    private void ApplyLiveMetrics(LiveSystemMetricsSnapshot snapshot, bool addHistory = true)
    {
        CpuUsagePercent = snapshot.CpuPercent ?? 0;
        GpuUsagePercent = snapshot.GpuPercent ?? 0;
        MemoryUsagePercent = snapshot.MemoryPercent ?? 0;
        DiskUsagePercent = snapshot.DiskPercent ?? 0;
        CpuUsageLabel = FormatLivePercent(snapshot.CpuPercent);
        GpuUsageLabel = FormatLivePercent(snapshot.GpuPercent);
        MemoryUsageLabel = FormatLivePercent(snapshot.MemoryPercent);
        DiskUsageLabel = FormatLivePercent(snapshot.DiskPercent);
        NetworkUsageLabel = localization.Format(
            "Dashboard.LivePerformance.NetworkValue",
            snapshot.NetworkThroughputMBps);
        MemoryUsageDetailLabel = snapshot is { UsedMemoryGiB: { } used, TotalMemoryGiB: { } total }
            ? localization.Format("Dashboard.LivePerformance.MemoryDetail", used, total)
            : string.Empty;
        LiveMetricsUpdatedLabel = localization.Format(
            "Dashboard.LivePerformance.Updated",
            snapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss"));

        if (addHistory)
        {
            AddMetricSample(cpuUsageHistory, snapshot.CpuPercent);
            AddMetricSample(gpuUsageHistory, snapshot.GpuPercent);
            CpuUsageSeries = cpuUsageHistory.ToArray();
            GpuUsageSeries = gpuUsageHistory.ToArray();
        }

        CpuTrendLabel = DescribeTrend(cpuUsageHistory);
        GpuTrendLabel = DescribeTrend(gpuUsageHistory);
    }

    /// <summary>
    /// Average and peak of the samples currently plotted. Both come from the
    /// same history the chart draws, so the summary never contradicts the line
    /// above it; an empty history reports no reading instead of "0%".
    /// </summary>
    private string DescribeTrend(Queue<double> history)
    {
        if (history.Count == 0)
        {
            return localization.GetString("Dashboard.LivePerformance.NotAvailable");
        }

        return localization.Format(
            "Dashboard.LivePerformance.TrendValue",
            history.Average(),
            history.Max());
    }

    private string FormatLivePercent(double? value) => value is { } available
        ? localization.Format("Dashboard.LivePerformance.PercentValue", available)
        : localization.GetString("Dashboard.LivePerformance.NotAvailable");

    private static void AddMetricSample(Queue<double> history, double? value)
    {
        if (value is null)
        {
            return;
        }

        history.Enqueue(Math.Clamp(value.Value, 0, 100));
        while (history.Count > LiveMetricsHistoryCapacity)
        {
            history.Dequeue();
        }
    }
}
