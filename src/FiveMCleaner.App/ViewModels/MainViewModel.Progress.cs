using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Core.Planning;

namespace FiveMCleaner.App.ViewModels;

public sealed partial class MainViewModel
{
    public double ProgressPercent
    {
        get => progressPercent;
        private set
        {
            if (SetProperty(ref progressPercent, value))
            {
                OnPropertyChanged(nameof(ProgressIntensity));
                OnPropertyChanged(nameof(ProgressPercentLabel));
            }
        }
    }

    /// <summary>
    /// Progresso real da execução mapeado para a faixa 0,3–1, que a cena 3D do
    /// Otimizador usa como velocidade e brilho. Não é uma medida nova: é o
    /// mesmo <see cref="ProgressPercent"/>, com um piso para que o núcleo nunca
    /// pareça parado nos primeiros segundos de uma execução que já começou.
    /// </summary>
    public string ProgressPercentLabel => $"{Math.Round(ProgressPercent, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.CurrentCulture)}%";

    public double ProgressIntensity => 0.3 + (Math.Clamp(ProgressPercent / 100d, 0, 1) * 0.7);

    public string ProgressHeadline
    {
        get => progressHeadline;
        private set
        {
            if (!string.Equals(progressHeadline, value, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(progressHeadline))
            {
                PreviousProgressHeadline = progressHeadline;
            }

            SetProperty(ref progressHeadline, value);
        }
    }

    public string PreviousProgressHeadline
    {
        get => previousProgressHeadline;
        private set
        {
            if (SetProperty(ref previousProgressHeadline, value))
            {
                OnPropertyChanged(nameof(HasPreviousProgressHeadline));
            }
        }
    }

    public bool HasPreviousProgressHeadline => !string.IsNullOrWhiteSpace(PreviousProgressHeadline);

    public string ElapsedTimeLabel { get => elapsedTimeLabel; private set => SetProperty(ref elapsedTimeLabel, value); }

    public string RemainingTimeLabel { get => remainingTimeLabel; private set => SetProperty(ref remainingTimeLabel, value); }

    private void TrackOptimizationTelemetry(
        string eventName,
        TimeSpan executionTime,
        string? errorCategory,
        BugCode? bugCode = null)
    {
        if (!telemetry.IsEnabled)
        {
            return;
        }

        var telemetryEvent = new AnonymousTelemetryEvent(
            eventName,
            executionTime,
            AppVersion.TrimStart('v', 'V'),
            errorCategory,
            OsVersion: diagnostic?.OsLabel,
            SystemArchitecture: diagnostic?.SystemArchitecture,
            CpuModel: ShareAnonymousTelemetry ? diagnostic?.CpuName : null,
            GpuModel: ShareAnonymousTelemetry ? diagnostic?.GpuName : null,
            RamBucketGiB: ShareAnonymousTelemetry && diagnostic is not null ? RamBucketCalculator.ComputeBucketGiB(diagnostic.TotalMemoryGiB) : null,
            Profile: ShareAnonymousTelemetry ? selectedProfile.ToString() : null,
            ActionIds: ShareAnonymousTelemetry ? currentPlan?.Actions
                .Select(action => action.Metadata.Id)
                .Take(TelemetryEventValidator.MaxActionIds)
                .ToArray() : null,
            BugCode: bugCode);
        _ = TrackOptimizationTelemetryAsync(telemetryEvent);
    }

    private async Task TrackOptimizationTelemetryAsync(AnonymousTelemetryEvent telemetryEvent)
    {
        try
        {
            await telemetry.TrackAsync(telemetryEvent).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Telemetria é opcional e não pode afetar a experiência nem gerar logs locais adicionais.
        }
    }

    private void ApplyProgress(AppProgressUpdate update)
    {
        ProgressPercent = Math.Clamp(update.Percent, 0, 100);
        EnqueueHeadline(update.Headline);
        if (update.ActionId is not null && update.Outcome is { } outcome
            && outcome != ActionExecutionOutcome.Pending)
        {
            UpsertStepLedgerItem(update.ActionId, outcome);
        }

        UpdateOperationTiming();
    }

    private void ClearProgressHistory()
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = null;
        pendingHeadlines.Clear();
        headlineShownAtUtc = default;
        previousProgressHeadline = string.Empty;
        progressHeadline = string.Empty;
        OnPropertyChanged(nameof(PreviousProgressHeadline));
        OnPropertyChanged(nameof(HasPreviousProgressHeadline));
        OnPropertyChanged(nameof(ProgressHeadline));
    }

    /// <summary>
    /// Cada passo do otimizador fica visível pelo menos <see cref="HeadlineMinimumDwell"/>
    /// antes de dar lugar ao próximo, em qualquer modo (leve/médio/agressivo).
    /// </summary>
    private void EnqueueHeadline(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline)
            || string.Equals(headline, ProgressHeadline, StringComparison.Ordinal)
            || (pendingHeadlines.Count > 0 && string.Equals(pendingHeadlines.Last(), headline, StringComparison.Ordinal)))
        {
            return;
        }

        pendingHeadlines.Enqueue(headline);
        AdvanceHeadlineQueue();
    }

    private void AdvanceHeadlineQueue()
    {
        if (headlineDwellTimer is not null || pendingHeadlines.Count == 0)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - headlineShownAtUtc;
        if (elapsed >= HeadlineMinimumDwell)
        {
            ShowNextQueuedHeadline();
        }
        else
        {
            StartHeadlineDwellTimer(HeadlineMinimumDwell - elapsed);
        }
    }

    private void ShowNextQueuedHeadline()
    {
        ProgressHeadline = pendingHeadlines.Dequeue();
        headlineShownAtUtc = DateTime.UtcNow;
        StartHeadlineDwellTimer(HeadlineMinimumDwell);
    }

    private void StartHeadlineDwellTimer(TimeSpan due)
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = new DispatcherTimer
        {
            Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1)
        };
        headlineDwellTimer.Tick += OnHeadlineDwellTimerTick;
        headlineDwellTimer.Start();
    }

    private void OnHeadlineDwellTimerTick(object? sender, EventArgs e)
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = null;

        if (pendingHeadlines.Count > 0)
        {
            ShowNextQueuedHeadline();
        }
    }

    /// <summary>
    /// Usado para estados terminais (concluído, cancelado, falhou): descarta a fila de
    /// passos pendentes e mostra o resultado final imediatamente, sem esperar o dwell.
    /// </summary>
    private void FinalizeHeadline(string headline)
    {
        headlineDwellTimer?.Stop();
        headlineDwellTimer = null;
        pendingHeadlines.Clear();
        ProgressHeadline = headline;
        headlineShownAtUtc = DateTime.UtcNow;
    }

    private void StartOperationTiming()
    {
        operationTimer?.Stop();
        operationStopwatch = Stopwatch.StartNew();
        progressTimingEstimator.Reset();
        UpdateOperationTiming();

        operationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        operationTimer.Tick += OperationTimerOnTick;
        operationTimer.Start();
    }

    private void OperationTimerOnTick(object? sender, EventArgs eventArgs)
    {
        UpdateOperationTiming();
    }

    private void StopOperationTiming(bool completedSuccessfully)
    {
        if (operationTimer is not null)
        {
            operationTimer.Stop();
            operationTimer.Tick -= OperationTimerOnTick;
            operationTimer = null;
        }

        if (operationStopwatch is null)
        {
            return;
        }

        operationStopwatch.Stop();
        var elapsed = operationStopwatch.Elapsed;
        ElapsedTimeLabel = localization.Format(
            "Progress.ElapsedFormat",
            FormatDuration(elapsed));
        RemainingTimeLabel = completedSuccessfully
            ? localization.Format("Progress.CompletedInFormat", FormatDuration(elapsed))
            : string.Empty;
        operationStopwatch = null;
        progressTimingEstimator.Reset();
    }

    private void UpdateOperationTiming()
    {
        if (operationStopwatch is null)
        {
            return;
        }

        var elapsed = operationStopwatch.Elapsed;
        ElapsedTimeLabel = localization.Format(
            "Progress.ElapsedFormat",
            FormatDuration(elapsed));

        if (ProgressPercent >= 99)
        {
            RemainingTimeLabel = localization.GetString("Progress.Finishing");
            return;
        }

        if (elapsed < TimeSpan.FromSeconds(2) || ProgressPercent < 3)
        {
            RemainingTimeLabel = localization.GetString("Progress.Calculating");
            return;
        }

        var estimate = progressTimingEstimator.EstimateRemaining(elapsed, ProgressPercent);
        if (estimate is null)
        {
            RemainingTimeLabel = localization.GetString("Progress.Calculating");
            return;
        }

        RemainingTimeLabel = localization.Format(
            "Progress.RemainingFormat",
            FormatDuration(estimate.Value));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var rounded = TimeSpan.FromSeconds(Math.Max(0, Math.Round(duration.TotalSeconds)));
        return rounded.TotalHours >= 1
            ? $"{(int)rounded.TotalHours:00}:{rounded.Minutes:00}:{rounded.Seconds:00}"
            : $"{rounded.Minutes:00}:{rounded.Seconds:00}";
    }
}
