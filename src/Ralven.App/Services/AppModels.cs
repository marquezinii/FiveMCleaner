using Ralven.Contracts;

namespace Ralven.App.Services;

public enum AppThemePreference
{
    System,
    Dark,
    Light
}

public enum PerformancePressureLevel
{
    Low,
    Moderate,
    High
}

public sealed record AppDiagnostic
{
    public required FiveMEdition Edition { get; init; }

    public required bool IsFiveMRunning { get; init; }

    public string? FiveMRoot { get; init; }

    public required bool GtaVDetected { get; init; }

    public required bool GtaVIsRunning { get; init; }

    public string? GtaVExecutablePath { get; init; }

    public required string GtaVGraphicsSettingsPath { get; init; }

    public required string CpuName { get; init; }

    public required string GpuName { get; init; }

    /// <summary>
    /// Every display adapter Windows reported during the local scan. The
    /// summary above remains for compatibility with existing callers; this
    /// list lets the UI present hybrid and multi-GPU machines accurately.
    /// </summary>
    public IReadOnlyList<string> GpuNames { get; init; } = [];

    public required double TotalMemoryGiB { get; init; }

    public required double AvailableMemoryGiB { get; init; }

    /// <summary>Physical DIMM layout reported by Windows when available.</summary>
    public string? MemoryModuleLayout { get; init; }

    public required int LogicalProcessorCount { get; init; }

    public required double FreeDiskGiB { get; init; }

    public required long LegacyCacheBytes { get; init; }

    public required string OsLabel { get; init; }

    public string SystemArchitecture { get; init; } = "Unknown";

    public required int ReadinessScore { get; init; }

    public required OptimizationProfile RecommendedProfile { get; init; }

    public required PerformancePressureLevel PerformancePressure { get; init; }

    public required StreamingSoftwareSnapshot StreamingSoftware { get; init; }

    public IReadOnlyList<string> Notices { get; init; } = [];
}

public enum AppProgressKind
{
    Preparing,
    Applying,
    Verifying,
    Completed,
    Warning,
    Failed,
    RollingBack
}

public sealed record AppProgressUpdate
{
    public required DateTimeOffset Timestamp { get; init; }

    public required AppProgressKind Kind { get; init; }

    public required double Percent { get; init; }

    public required string Headline { get; init; }

    public required string Detail { get; init; }

    public string? ActionId { get; init; }

    /// <summary>1-based index of the current step, or 0 when not step-based.</summary>
    public int CompletedSteps { get; init; }

    /// <summary>Total number of steps in the current run, or 0 when unknown.</summary>
    public int TotalSteps { get; init; }

    /// <summary>Outcome of the step this update refers to, when applicable.</summary>
    public ActionExecutionOutcome? Outcome { get; init; }
}

public sealed record AppOptimizationResult
{
    public required Guid TransactionId { get; init; }

    public required bool Succeeded { get; init; }

    public required bool WasCancelled { get; init; }

    public required string Summary { get; init; }

    public required int CompletedActions { get; init; }

    public required long BytesFreed { get; init; }

    /// <summary>Structured report of the run, when a journal was produced.</summary>
    public OptimizationReportDto? Report { get; init; }

    /// <summary>Before/after resource comparison, when one could be captured.</summary>
    public OptimizationComparisonResult? Comparison { get; init; }

    /// <summary>Allowlisted run-level cause, when the service observed one directly.</summary>
    public BugCode? FailureBugCode { get; init; }

    /// <summary>Allowlisted run-level category, without exception text.</summary>
    public string? FailureErrorCategory { get; init; }
}

/// <summary>
/// A lightweight, best-effort resource reading taken immediately before or
/// shortly after an optimization run. It is not a benchmark and never
/// includes FPS: this is a coarse safety-net signal, not a performance
/// guarantee.
/// </summary>
public sealed record ResourceComparisonSnapshot
{
    public required DateTimeOffset CapturedAtUtc { get; init; }

    public double? CpuPercent { get; init; }

    public double? GpuPercent { get; init; }

    public double? DiskPercent { get; init; }

    public required double AvailableMemoryGiB { get; init; }

    public bool ThermalElevated { get; init; }

    public bool NetworkIssuesDetected { get; init; }
}

/// <summary>
/// One iteration of the official GTA V standalone <c>-benchmark</c> run,
/// mirrored from <see cref="Ralven.Windows.Infrastructure.GtaVBenchmarkIterationResult"/>
/// so the App layer's public surface does not depend on Windows.Infrastructure types.
/// </summary>
public sealed record AppGtaVBenchmarkIteration(
    double AverageFps,
    double MinimumFps,
    double OnePercentLowFps,
    double PointOnePercentLowFps,
    double AverageFrametimeMs,
    double PeakFrametimeMs,
    int SampleCount);

public sealed record AppGtaVBenchmarkResult
{
    public required bool Succeeded { get; init; }

    public string? FailureReason { get; init; }

    public required IReadOnlyList<AppGtaVBenchmarkIteration> Iterations { get; init; }

    public AppGtaVBenchmarkIteration? Median { get; init; }
}

public sealed record OptimizationComparisonResult
{
    public required string HardwareProfileSignature { get; init; }

    public required ResourceComparisonSnapshot Before { get; init; }

    public required ResourceComparisonSnapshot After { get; init; }

    /// <summary>
    /// True only for signals this product can attribute with reasonable
    /// confidence to something having gotten worse (a new thermal signal, or
    /// available memory dropping sharply) — never derived from FPS, which
    /// this product does not measure during a live FiveM session.
    /// </summary>
    public required bool RegressionSuspected { get; init; }

    public required IReadOnlyList<string> RegressionReasons { get; init; }
}

public enum AppHistoryKind
{
    Optimization = 0,
    WindowsGaming = 1
}

public sealed record AppHistoryRecord
{
    public required Guid TransactionId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required OptimizationProfile Profile { get; init; }

    public PersonalUsage? PersonalUsage { get; init; }

    public AppHistoryKind Kind { get; init; }

    public required string State { get; init; }

    public required int ChangedActions { get; init; }

    public required bool CanRollback { get; init; }
}

public sealed record AppSettings
{
    public AppLanguagePreference Language { get; init; } = AppLanguagePreference.Automatic;

    public AppThemePreference Theme { get; init; } = AppThemePreference.System;

    public bool MinimizeToTrayOnClose { get; init; } = true;

    public bool LaunchAtStartup { get; init; } = true;

    /// <summary>
    /// Whether a launch requested by the Windows startup entry should remain
    /// in the notification area. New and legacy settings default to a visible
    /// window until the user explicitly enables this preference.
    /// </summary>
    public bool? StartMinimized { get; init; } = false;

    public bool CheckForUpdates { get; init; } = true;

    public bool NotifyWhenUpdateAvailable { get; init; } = true;

    /// <summary>
    /// Preferência persistida legada que, junto de
    /// <see cref="ShareCrashReports"/>, representa a opção única de relatórios
    /// técnicos opcionais. Os dois campos são mantidos para compatibilidade
    /// com configurações existentes e nascem habilitados em instalações novas.
    /// </summary>
    public bool ShareAnonymousTelemetry { get; init; } = true;

    /// <summary>
    /// Segundo campo de compatibilidade da preferência única de relatórios
    /// opcionais. Um valor legado falso prevalece para preservar opt-out.
    /// </summary>
    public bool ShareCrashReports { get; init; } = true;

    /// <summary>
    /// Versão do consentimento de privacidade confirmada explicitamente pelo
    /// usuário na tela dedicada. <see langword="null"/> significa que o
    /// usuário ainda não confirmou nenhuma versão (instalação nova ou
    /// instalação antiga anterior à existência desse campo) — nesse estado,
    /// nenhum envio de telemetria ou crash report é autorizado, independente
    /// do valor dos booleanos acima. Ver <see cref="PrivacyConsentPolicy"/>.
    /// </summary>
    public int? PrivacyConsentVersion { get; init; }

    /// <summary>
    /// Id (o <c>updated_at</c> do servidor) do último aviso ao vivo que o
    /// usuário fechou explicitamente. <see langword="null"/> significa que
    /// nenhum aviso foi dispensado ainda. Usado só para não reexibir o
    /// banner de um aviso já lido; o ícone de alerta no canto continua
    /// visível enquanto o aviso seguir ativo no servidor.
    /// </summary>
    public string? DismissedLiveAlertId { get; init; }

    /// <summary>
    /// App version (for example "1.3.2") whose "What's New" panel was last
    /// shown to (or silently acknowledged by) the user. <see langword="null"/>
    /// means no version has been recorded yet — either a brand-new
    /// installation or an installation from before this field existed. See
    /// <see cref="ReleaseNotesEvaluator"/>; deliberately not a boolean flag,
    /// so a future version with new notes shows the panel again instead of
    /// staying silenced forever.
    /// </summary>
    public string? LastSeenReleaseNotesVersion { get; init; }
}
