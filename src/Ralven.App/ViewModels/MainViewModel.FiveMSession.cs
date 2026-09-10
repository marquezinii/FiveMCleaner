using System.Globalization;
using System.Windows.Threading;
using Ralven.App.Services;
using Ralven.Contracts;
using Ralven.Windows.Infrastructure;

namespace Ralven.App.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly TimeSpan FiveMSessionPollInterval = TimeSpan.FromSeconds(5);
    private readonly Func<string, FiveMSessionPresence> fiveMSessionProbe;
    private readonly FiveMSessionStateTracker fiveMSessionTracker = new();
    private DispatcherTimer? fiveMSessionTimer;
    private bool fiveMSessionProbeInProgress;
    private int fiveMSessionGeneration;
    private string? fiveMSessionRoot;
    private FiveMSessionPresence? lastFiveMSessionPresence;
    private bool isFiveMSessionMonitoring;
    private string fiveMSessionStatusLabel = string.Empty;
    private string fiveMSessionDetailLabel = string.Empty;
    private string fiveMSessionActionLabel = string.Empty;
    private string fiveMSessionStatusBrushKey = "TextTertiaryBrush";

    public bool IsFiveMSessionMonitoring
    {
        get => isFiveMSessionMonitoring;
        private set => SetProperty(ref isFiveMSessionMonitoring, value);
    }

    public bool IsFiveMSessionActive => fiveMSessionTracker.IsActive;

    public bool IsFiveMSessionEndConfirmationPending => fiveMSessionTracker.IsEndConfirmationPending;

    public bool IsFiveMSessionReadUnavailable => IsFiveMSessionMonitoring
        && lastFiveMSessionPresence == FiveMSessionPresence.Indeterminate;

    public bool CanToggleFiveMSessionMonitor => IsFiveMSessionMonitoring || HasLegacySessionRoot();

    public string FiveMSessionStatusLabel
    {
        get => fiveMSessionStatusLabel;
        private set => SetProperty(ref fiveMSessionStatusLabel, value);
    }

    public string FiveMSessionDetailLabel
    {
        get => fiveMSessionDetailLabel;
        private set => SetProperty(ref fiveMSessionDetailLabel, value);
    }

    public string FiveMSessionActionLabel
    {
        get => fiveMSessionActionLabel;
        private set => SetProperty(ref fiveMSessionActionLabel, value);
    }

    public string FiveMSessionStatusBrushKey
    {
        get => fiveMSessionStatusBrushKey;
        private set => SetProperty(ref fiveMSessionStatusBrushKey, value);
    }

    public void ToggleFiveMSessionMonitor()
    {
        if (IsFiveMSessionMonitoring)
        {
            StopFiveMSessionMonitor();
            return;
        }

        if (!HasLegacySessionRoot())
        {
            RefreshFiveMSessionMonitorPresentation();
            return;
        }

        fiveMSessionRoot = diagnostic!.FiveMRoot;
        fiveMSessionTracker.Reset();
        lastFiveMSessionPresence = null;
        IsFiveMSessionMonitoring = true;
        RefreshFiveMSessionMonitorPresentation();

        fiveMSessionTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FiveMSessionPollInterval
        };
        fiveMSessionTimer.Tick -= FiveMSessionTimer_Tick;
        fiveMSessionTimer.Tick += FiveMSessionTimer_Tick;
        fiveMSessionTimer.Start();
        _ = ProbeFiveMSessionAsync();
    }

    private void FiveMSessionTimer_Tick(object? sender, EventArgs e) => _ = ProbeFiveMSessionAsync();

    // Cada tick é independente e nunca deixa uma falha de leitura interromper
    // o monitoramento: uma exceção vira apresentação "indisponível" para essa
    // rodada e o timer segue tentando na próxima, seguindo o mesmo padrão de
    // CaptureLiveMetricsAsync.
    private async Task ProbeFiveMSessionAsync()
    {
        if (!isFiveMSessionMonitoring
            || fiveMSessionProbeInProgress
            || fiveMSessionRoot is not { } root)
        {
            return;
        }

        fiveMSessionProbeInProgress = true;
        var generation = fiveMSessionGeneration;
        var previousState = (lastFiveMSessionPresence, fiveMSessionTracker.IsActive, fiveMSessionTracker.IsEndConfirmationPending);
        try
        {
            try
            {
                var presence = await Task.Run(() => fiveMSessionProbe(root));
                if (!isFiveMSessionMonitoring || generation != fiveMSessionGeneration)
                {
                    return;
                }

                lastFiveMSessionPresence = presence;
                fiveMSessionTracker.Observe(presence, DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                if (!isFiveMSessionMonitoring || generation != fiveMSessionGeneration)
                {
                    return;
                }

                lastFiveMSessionPresence = FiveMSessionPresence.Indeterminate;
                fiveMSessionTracker.Observe(FiveMSessionPresence.Indeterminate, DateTimeOffset.UtcNow);
            }

            if (liveMetricsEnabled
                || previousState != (lastFiveMSessionPresence, fiveMSessionTracker.IsActive, fiveMSessionTracker.IsEndConfirmationPending))
            {
                RefreshFiveMSessionMonitorPresentation();
            }
        }
        finally
        {
            fiveMSessionProbeInProgress = false;
            if (isFiveMSessionMonitoring && generation != fiveMSessionGeneration)
            {
                _ = ProbeFiveMSessionAsync();
            }
        }
    }

    private void StopFiveMSessionMonitor()
    {
        fiveMSessionGeneration++;
        fiveMSessionTimer?.Stop();
        fiveMSessionRoot = null;
        lastFiveMSessionPresence = null;
        fiveMSessionTracker.Reset();
        IsFiveMSessionMonitoring = false;
        RefreshFiveMSessionMonitorPresentation();
    }

    private void RefreshFiveMSessionMonitorAvailability()
    {
        if (!HasLegacySessionRoot() && liveMetricsTarget == LiveMetricsTarget.FiveM)
        {
            SelectLiveMetricsTarget(LiveMetricsTarget.System);
        }

        if (IsFiveMSessionMonitoring
            && (!HasLegacySessionRoot()
                || !string.Equals(
                    fiveMSessionRoot,
                    diagnostic!.FiveMRoot,
                    StringComparison.OrdinalIgnoreCase)))
        {
            StopFiveMSessionMonitor();
            return;
        }

        RefreshFiveMSessionMonitorPresentation();
        OnPropertyChanged(nameof(IsFiveMLiveMetricsTargetAvailable));
    }

    private void RefreshFiveMSessionMonitorPresentation()
    {
        FiveMSessionActionLabel = localization.GetString(
            IsFiveMSessionMonitoring
                ? "Dashboard.SessionMonitor.Stop"
                : "Dashboard.SessionMonitor.Start");

        if (!HasLegacySessionRoot())
        {
            SetFiveMSessionPresentation(
                "Dashboard.SessionMonitor.Unavailable",
                "Dashboard.SessionMonitor.UnavailableDetail",
                "TextTertiaryBrush");
        }
        else if (!IsFiveMSessionMonitoring)
        {
            SetFiveMSessionPresentation(
                "Dashboard.SessionMonitor.Off",
                "Dashboard.SessionMonitor.OffDetail",
                "TextSecondaryBrush");
        }
        else if (IsFiveMSessionReadUnavailable)
        {
            SetFiveMSessionPresentation(
                "Dashboard.SessionMonitor.ReadUnavailable",
                "Dashboard.SessionMonitor.ReadUnavailableDetail",
                "WarningBaseBrush");
        }
        else if (fiveMSessionTracker.IsEndConfirmationPending)
        {
            SetFiveMSessionPresentation(
                "Dashboard.SessionMonitor.ConfirmingEnd",
                "Dashboard.SessionMonitor.ConfirmingEndDetail",
                "WarningBaseBrush");
        }
        else if (fiveMSessionTracker.IsActive)
        {
            FiveMSessionStatusLabel = localization.GetString("Dashboard.SessionMonitor.Active");
            FiveMSessionDetailLabel = localization.Format(
                "Dashboard.SessionMonitor.ActiveDetail",
                FormatSessionDuration(DateTimeOffset.UtcNow - fiveMSessionTracker.StartedAt!.Value));
            FiveMSessionStatusBrushKey = "SuccessBaseBrush";
        }
        else if (fiveMSessionTracker.HasCompletedSession)
        {
            FiveMSessionStatusLabel = localization.GetString("Dashboard.SessionMonitor.Completed");
            FiveMSessionDetailLabel = localization.Format(
                "Dashboard.SessionMonitor.CompletedDetail",
                FormatSessionDuration(fiveMSessionTracker.LastDuration ?? TimeSpan.Zero));
            FiveMSessionStatusBrushKey = "InfoBaseBrush";
        }
        else
        {
            SetFiveMSessionPresentation(
                "Dashboard.SessionMonitor.Waiting",
                "Dashboard.SessionMonitor.WaitingDetail",
                "InfoBaseBrush");
        }

        OnPropertyChanged(nameof(IsFiveMSessionActive));
        OnPropertyChanged(nameof(IsFiveMSessionEndConfirmationPending));
        OnPropertyChanged(nameof(IsFiveMSessionReadUnavailable));
        OnPropertyChanged(nameof(CanToggleFiveMSessionMonitor));
        RaiseCommandState();
    }

    private void SetFiveMSessionPresentation(string statusKey, string detailKey, string brushKey)
    {
        FiveMSessionStatusLabel = localization.GetString(statusKey);
        FiveMSessionDetailLabel = localization.GetString(detailKey);
        FiveMSessionStatusBrushKey = brushKey;
    }

    private bool HasLegacySessionRoot() => diagnostic is
    {
        Edition: FiveMEdition.Legacy,
        FiveMRoot: not null
    } && !string.IsNullOrWhiteSpace(diagnostic.FiveMRoot);

    private static string FormatSessionDuration(TimeSpan duration)
    {
        var normalized = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)normalized.TotalHours:00}:{normalized.Minutes:00}:{normalized.Seconds:00}");
    }
}
