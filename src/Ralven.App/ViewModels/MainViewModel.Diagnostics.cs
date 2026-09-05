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
    private string systemPcStatusKey = "System.Pc.Status.Loading";

    public string CpuName { get => cpuName; private set => SetProperty(ref cpuName, value); }

    public string RamLabel { get => ramLabel; private set => SetProperty(ref ramLabel, value); }

    public string DiskLabel { get => diskLabel; private set => SetProperty(ref diskLabel, value); }

    public string WindowsLabel { get => windowsLabel; private set => SetProperty(ref windowsLabel, value); }

    public string GpuDetail { get => gpuDetail; private set => SetProperty(ref gpuDetail, value); }

    public string ReadinessScoreExplanation { get => readinessScoreExplanation; private set => SetProperty(ref readinessScoreExplanation, value); }

    public string EditionLabel { get => editionLabel; private set => SetProperty(ref editionLabel, value); }

    public string EditionBadgeLabel { get => editionBadgeLabel; private set => SetProperty(ref editionBadgeLabel, value); }

    public string GtaStatusLabel { get => gtaStatusLabel; private set => SetProperty(ref gtaStatusLabel, value); }

    public bool IsFiveMLegacyDetected { get => isFiveMLegacyDetected; private set => SetProperty(ref isFiveMLegacyDetected, value); }

    public bool IsGtaVLegacyDetected { get => isGtaVLegacyDetected; private set => SetProperty(ref isGtaVLegacyDetected, value); }

    public string RecommendationTitle { get => recommendationTitle; private set => SetProperty(ref recommendationTitle, value); }

    public string RecommendationText { get => recommendationText; private set => SetProperty(ref recommendationText, value); }

    public string StreamingReadinessTitle { get => streamingReadinessTitle; private set => SetProperty(ref streamingReadinessTitle, value); }

    public string StreamingReadinessDetail { get => streamingReadinessDetail; private set => SetProperty(ref streamingReadinessDetail, value); }

    public string ReadinessLevelLabel { get => readinessLevelLabel; private set => SetProperty(ref readinessLevelLabel, value); }

    /// <summary>Logical processor count reported by the local scan, as a bare number.</summary>
    public string LogicalProcessorLabel { get => logicalProcessorLabel; private set => SetProperty(ref logicalProcessorLabel, value); }

    public string LogicalProcessorDetail { get => logicalProcessorDetail; private set => SetProperty(ref logicalProcessorDetail, value); }

    /// <summary>Free physical memory at scan time (e.g. "12,4 GB").</summary>
    public string AvailableMemoryLabel { get => availableMemoryLabel; private set => SetProperty(ref availableMemoryLabel, value); }

    public string AvailableMemoryDetail { get => availableMemoryDetail; private set => SetProperty(ref availableMemoryDetail, value); }

    /// <summary>
    /// Size of the FiveM server cache found on disk. This is the single number
    /// that most often explains why the optimizer has something to do, so the
    /// overview shows it instead of leaving the user to guess.
    /// </summary>
    public string LegacyCacheLabel { get => legacyCacheLabel; private set => SetProperty(ref legacyCacheLabel, value); }

    public string LegacyCacheDetail { get => legacyCacheDetail; private set => SetProperty(ref legacyCacheDetail, value); }

    public string PerformancePressureLabel { get => performancePressureLabel; private set => SetProperty(ref performancePressureLabel, value); }

    public string PerformancePressureBrushKey { get => performancePressureBrushKey; private set => SetProperty(ref performancePressureBrushKey, value); }

    /// <summary>When the last local scan finished, already localized.</summary>
    public string LastScanLabel { get => lastScanLabel; private set => SetProperty(ref lastScanLabel, value); }

    public string SystemPcStatusMessage => localization.GetString(systemPcStatusKey);

    /// <summary>
    /// "Boa tarde, Felipe. O que iremos fazer hoje?" — greets by local time of
    /// day, with the first name only when a session is signed in and the
    /// account has one on file. See <see cref="RefreshGreeting"/>.
    /// </summary>
    public string GreetingTitle { get => greetingTitle; private set => SetProperty(ref greetingTitle, value); }

    public string LastOptimizationTitle { get => lastOptimizationTitle; private set => SetProperty(ref lastOptimizationTitle, value); }

    public string LastOptimizationDateLabel { get => lastOptimizationDateLabel; private set => SetProperty(ref lastOptimizationDateLabel, value); }

    public string LastOptimizationSummary { get => lastOptimizationSummary; private set => SetProperty(ref lastOptimizationSummary, value); }

    /// <summary>False when this machine has never completed an optimization.</summary>
    public bool HasLastOptimization { get => hasLastOptimization; private set => SetProperty(ref hasLastOptimization, value); }

    public int ReadinessScore { get => readinessScore; private set => SetProperty(ref readinessScore, value); }

    private void ApplyDiagnostic(AppDiagnostic value)
    {
        diagnostic = value;
        diagnosticFailed = false;
        OnPropertyChanged(nameof(IsLightRecommended));
        OnPropertyChanged(nameof(IsBalancedRecommended));
        OnPropertyChanged(nameof(IsAggressiveRecommended));
        OnPropertyChanged(nameof(EmptyPlanMessage));
        OnPropertyChanged(nameof(SelectedProfileLabel));
        OnPropertyChanged(nameof(SelectedProfileName));
        OnPropertyChanged(nameof(IsSelectedProfileRecommended));
        if (!profileInitializedFromDiagnostic)
        {
            selectedProfile = value.RecommendedProfile;
            profileInitializedFromDiagnostic = true;
            OnPropertyChanged(nameof(IsLightSelected));
            OnPropertyChanged(nameof(IsBalancedSelected));
            OnPropertyChanged(nameof(IsAggressiveSelected));
            OnPropertyChanged(nameof(SelectedProfileLabel));
            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(IsSelectedProfileRecommended));
            OnPropertyChanged(nameof(ProfileIntensity));
            OnPropertyChanged(nameof(ProfileIntensityPercent));
        }

        CpuName = value.CpuName;
        GpuDetail = value.GpuNames.Count > 1
            ? string.Join(Environment.NewLine, value.GpuNames)
            : value.GpuName;
        RamLabel = string.IsNullOrWhiteSpace(value.MemoryModuleLayout)
            ? localization.Format("Diagnosis.MemoryTotal", value.TotalMemoryGiB)
            : localization.Format("Diagnosis.MemoryModules", value.TotalMemoryGiB, value.MemoryModuleLayout);
        DiskLabel = localization.Format("Diagnosis.DiskCapacity", value.FreeDiskGiB);
        WindowsLabel = value.OsLabel;
        LogicalProcessorLabel = value.LogicalProcessorCount.ToString(localization.CurrentCulture);
        LogicalProcessorDetail = localization.GetString("Dashboard.Kpi.Cores.Detail");
        AvailableMemoryLabel = localization.Format("Dashboard.Kpi.GigabyteValue", value.AvailableMemoryGiB);
        AvailableMemoryDetail = localization.Format("Dashboard.Kpi.Memory.Detail", value.TotalMemoryGiB);
        (LegacyCacheLabel, LegacyCacheDetail) = DescribeLegacyCache(value.LegacyCacheBytes);
        PerformancePressureLabel = value.PerformancePressure switch
        {
            PerformancePressureLevel.Low => localization.GetString("Dashboard.Pressure.Low"),
            PerformancePressureLevel.High => localization.GetString("Dashboard.Pressure.High"),
            _ => localization.GetString("Dashboard.Pressure.Moderate")
        };
        PerformancePressureBrushKey = value.PerformancePressure switch
        {
            PerformancePressureLevel.Low => "SuccessBaseBrush",
            PerformancePressureLevel.High => "DangerBaseBrush",
            _ => "WarningBaseBrush"
        };
        LastScanLabel = localization.Format(
            "Dashboard.LastScan",
            DateTime.Now.ToString("HH:mm", localization.CurrentCulture));
        ReadinessScoreExplanation = localization.GetString("Dashboard.ReadinessExplanation");
        ReadinessScore = value.ReadinessScore;
        ReadinessLevelLabel = ReadinessScore switch
        {
            > 75 => localization.GetString("Dashboard.Readiness.Excellent"),
            > 50 => localization.GetString("Dashboard.Readiness.Good"),
            > 25 => localization.GetString("Dashboard.Readiness.Average"),
            > 5 => localization.GetString("Dashboard.Readiness.Poor"),
            _ => localization.GetString("Dashboard.Readiness.VeryPoor")
        };
        IsFiveMLegacyDetected = value.Edition == FiveMEdition.Legacy;
        IsGtaVLegacyDetected = value.GtaVDetected || File.Exists(value.GtaVGraphicsSettingsPath);
        EditionLabel = value.Edition switch
        {
            FiveMEdition.Legacy => localization.GetString("Diagnosis.FiveMLegacyDetected"),
            FiveMEdition.Enhanced => localization.GetString("Diagnosis.FiveMEnhancedBlocked"),
            _ => localization.GetString("Diagnosis.FiveMNotFound")
        };
        EditionBadgeLabel = value.Edition switch
        {
            FiveMEdition.Legacy => "LEGACY",
            FiveMEdition.Enhanced => "ENHANCED",
            _ => localization.GetString("Status.Waiting")
        };
        GtaStatusLabel = IsGtaVLegacyDetected
            ? localization.GetString("Diagnosis.GtaVLegacyDetected")
            : localization.GetString("Diagnosis.GtaVNotFound");
        RecommendationTitle = value.IsFiveMRunning
            ? localization.GetString("Diagnosis.CloseFiveMSafely")
            : value.GtaVIsRunning
                ? localization.GetString("Diagnosis.CloseGtaVSafely")
                : localization.Format(
                    "Diagnosis.RecommendedProfile",
                    ProfileName(value.RecommendedProfile));
        RecommendationText = localization.GetString(value.Edition == FiveMEdition.Enhanced
            ? "Diagnosis.EnhancedUnsupported"
            : "Diagnosis.GeneralReady");
        ApplyStreamingReadiness(value);
        RefreshFiveMSessionMonitorAvailability();
    }

    private void SetSystemPcStatus(string resourceKey)
    {
        systemPcStatusKey = resourceKey;
        OnPropertyChanged(nameof(SystemPcStatusMessage));
    }

    /// <summary>
    /// Formats the FiveM cache footprint for the overview. Below one gibibyte
    /// the value is shown in mebibytes so a small cache does not collapse into
    /// "0,0 GB"; a missing installation reports no size instead of a zero.
    /// </summary>
    private (string Value, string Detail) DescribeLegacyCache(long bytes)
    {
        if (bytes <= 0)
        {
            return (
                localization.GetString("Dashboard.Kpi.Cache.None"),
                localization.GetString("Dashboard.Kpi.Cache.NoneDetail"));
        }

        const double bytesPerMiB = 1024d * 1024;
        var value = bytes >= 1024L * 1024 * 1024
            ? localization.Format("Dashboard.Kpi.GigabyteValue", bytes / (bytesPerMiB * 1024))
            : localization.Format("Dashboard.Kpi.MegabyteValue", bytes / bytesPerMiB);
        return (value, localization.GetString("Dashboard.Kpi.Cache.Detail"));
    }

    private void ApplyStreamingReadiness(AppDiagnostic value)
    {
        var assessment = StreamingReadinessAdvisor.Evaluate(value);
        (StreamingReadinessTitle, StreamingReadinessDetail) = assessment.Level switch
        {
            StreamingReadinessLevel.Protected => (
                localization.GetString("Streaming.Readiness.Protected.Title"),
                localization.GetString("Streaming.Readiness.Protected.Detail")),
            StreamingReadinessLevel.Attention => (
                localization.GetString("Streaming.Readiness.Attention.Title"),
                localization.GetString("Streaming.Readiness.Attention.Detail")),
            StreamingReadinessLevel.Ready => (
                localization.GetString("Streaming.Readiness.Ready.Title"),
                localization.GetString("Streaming.Readiness.Ready.Detail")),
            StreamingReadinessLevel.Partial => (
                localization.GetString("Streaming.Readiness.Partial.Title"),
                localization.GetString("Streaming.Readiness.Partial.Detail")),
            _ => (
                localization.GetString("Streaming.Readiness.NotDetected.Title"),
                localization.GetString("Streaming.Readiness.NotDetected.Detail"))
        };

        StreamingReadinessItems.Clear();
        foreach (var check in assessment.Checks)
        {
            StreamingReadinessItems.Add(CreateStreamingReadinessItem(check));
        }
    }

    private StreamingReadinessDisplayItem CreateStreamingReadinessItem(StreamingReadinessCheck check)
    {
        var suffix = check.Kind switch
        {
            StreamingReadinessCheckKind.Software => check.Tone switch
            {
                StreamingReadinessTone.Protected => "Protected",
                StreamingReadinessTone.Caution => "Partial",
                StreamingReadinessTone.Ready => "Detected",
                _ => "NotDetected"
            },
            StreamingReadinessCheckKind.Resources => check.Tone switch
            {
                StreamingReadinessTone.Ready => "Ready",
                StreamingReadinessTone.Caution => "Attention",
                _ => "Review"
            },
            StreamingReadinessCheckKind.GameSession => check.Tone == StreamingReadinessTone.Caution
                ? "Open"
                : "Closed",
            _ => throw new ArgumentOutOfRangeException(nameof(check))
        };
        var icon = check.Kind switch
        {
            StreamingReadinessCheckKind.Software => "IconStream",
            StreamingReadinessCheckKind.Resources => "IconPulse",
            StreamingReadinessCheckKind.GameSession => "IconGame",
            _ => "IconInfo"
        };
        var tone = check.Tone switch
        {
            StreamingReadinessTone.Protected => "SuccessBaseBrush",
            StreamingReadinessTone.Ready => "SuccessBaseBrush",
            StreamingReadinessTone.Caution => "WarningBaseBrush",
            _ => "TextTertiaryBrush"
        };
        var title = localization.GetString($"Streaming.Check.{check.Kind}.{suffix}.Title");
        var detail = check.Kind == StreamingReadinessCheckKind.Software
            && check.ApplicationNames.Count > 0
            ? localization.Format(
                $"Streaming.Check.{check.Kind}.{suffix}.DetailWithNames",
                string.Join(", ", check.ApplicationNames))
            : localization.GetString($"Streaming.Check.{check.Kind}.{suffix}.Detail");

        return new StreamingReadinessDisplayItem(icon, title, detail, tone);
    }

    private void ApplyHistory(IReadOnlyList<AppHistoryRecord> records)
    {
        historyRecords = records;
        HistoryItems.Clear();
        foreach (var record in records.OrderByDescending(item => item.CreatedAt).Take(30))
        {
            HistoryItems.Add(new HistoryDisplayItem(
                record.TransactionId,
                HistoryTitle(record),
                record.CreatedAt.LocalDateTime.ToString("g", localization.CurrentCulture),
                localization.Format("History.AdjustmentsState", record.ChangedActions, record.State),
                record.CanRollback,
                record.Kind));
        }

        // A composição vazia (silhueta do núcleo + texto) vive na própria
        // página; a coleção precisa continuar realmente vazia para que ela
        // apareça, em vez de uma linha de ledger fantasma com "Desfazer"
        // desabilitado — reverter uma execução que não existe não faz sentido.
        ApplyLastOptimization(records);
        OnPropertyChanged(nameof(CanRevertLastOptimization));
    }

    /// <summary>
    /// Summarizes the most recent run for the overview. With no history at all
    /// the card explains that state instead of disappearing and leaving a gap
    /// in the page.
    /// </summary>
    private void ApplyLastOptimization(IReadOnlyList<AppHistoryRecord> records)
    {
        var latest = records
            .Where(item => item.Kind == AppHistoryKind.Optimization)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();

        HasLastOptimization = latest is not null;
        if (latest is null)
        {
            LastOptimizationTitle = localization.GetString("Dashboard.LastRun.None.Title");
            LastOptimizationDateLabel = string.Empty;
            LastOptimizationSummary = localization.GetString("Dashboard.LastRun.None.Detail");
            return;
        }

        LastOptimizationTitle = HistoryTitle(latest);
        LastOptimizationDateLabel = latest.CreatedAt.LocalDateTime.ToString("g", localization.CurrentCulture);
        LastOptimizationSummary = localization.Format(
            "History.AdjustmentsState",
            latest.ChangedActions,
            latest.State);
    }

    private string HistoryTitle(AppHistoryRecord record)
    {
        return record.Kind == AppHistoryKind.WindowsGaming
            ? localization.GetString("History.WindowsGamingTitle")
            : localization.Format("History.ProfileTitle", record.PersonalUsage is { } usage
                ? localization.GetString("Ultra.Name") + " · " + localization.GetString($"Ultra.Usage.{usage}")
                : ProfileName(record.Profile));
    }

    /// <summary>
    /// Called by the window whenever the signed-in account's own profile is
    /// (re)read from the Worker — on login and on quiet session restore —
    /// and with <see langword="null"/> on sign-out. Firebase Authentication
    /// REST never stores a first name, so this is the only path that can
    /// ever populate it.
    /// </summary>
    public void SetAccountFirstName(string? firstName)
    {
        accountFirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName;
        RefreshGreeting();
    }

    /// <summary>
    /// Recomputes <see cref="GreetingTitle"/> from the machine's local clock.
    /// Boundaries: 06:00–11:59 morning, 12:00–17:59 afternoon, otherwise
    /// evening/night (18:00–05:59) — a plain three-way split a player reads
    /// the same way they would read a clock, not a technical period name.
    /// </summary>
    private void RefreshGreeting()
    {
        var hour = DateTime.Now.Hour;
        var period = hour switch
        {
            >= 6 and < 12 => "Morning",
            >= 12 and < 18 => "Afternoon",
            _ => "Evening"
        };
        GreetingTitle = accountFirstName is { } name
            ? localization.Format($"Greeting.{period}.WithName", name)
            : localization.GetString($"Greeting.{period}.NoName");
    }
}
