namespace FiveMCleaner.App.Services;

/// <summary>
/// Produz sinais locais e explicáveis para criadores sem inspecionar uma live,
/// encoder, cenas, contas ou arquivos dos aplicativos de transmissão.
/// </summary>
public static class StreamingReadinessAdvisor
{
    public static StreamingReadinessAssessment Evaluate(AppDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var software = diagnostic.StreamingSoftware;
        var runningApplications = software.Applications
            .Where(application => application.IsProcessRunning)
            .Select(application => application.DisplayName)
            .ToArray();
        var detectedApplications = software.Applications
            .Where(application => application.IsDetected)
            .Select(application => application.DisplayName)
            .ToArray();

        var checks = new List<StreamingReadinessCheck>
        {
            new(
                StreamingReadinessCheckKind.Software,
                software.IsPartial
                    ? StreamingReadinessTone.Caution
                    : runningApplications.Length > 0
                        ? StreamingReadinessTone.Protected
                        : detectedApplications.Length > 0
                            ? StreamingReadinessTone.Ready
                            : StreamingReadinessTone.Neutral,
                detectedApplications),
            new(
                StreamingReadinessCheckKind.Resources,
                GetResourceTone(diagnostic),
                []),
            new(
                StreamingReadinessCheckKind.GameSession,
                diagnostic.IsFiveMRunning || diagnostic.GtaVIsRunning
                    ? StreamingReadinessTone.Caution
                    : StreamingReadinessTone.Ready,
                [])
        };

        var level = software.IsPartial
            ? StreamingReadinessLevel.Partial
            : runningApplications.Length > 0
                ? StreamingReadinessLevel.Protected
                : checks.Any(check => check.Tone == StreamingReadinessTone.Caution)
                    ? StreamingReadinessLevel.Attention
                    : detectedApplications.Length > 0
                        ? StreamingReadinessLevel.Ready
                        : StreamingReadinessLevel.NotDetected;

        return new StreamingReadinessAssessment(
            level,
            checks,
            software.HasKnownSoftwareInstalled,
            software.HasKnownProcessRunning);
    }

    private static StreamingReadinessTone GetResourceTone(AppDiagnostic diagnostic)
    {
        if (diagnostic.AvailableMemoryGiB < 4 || diagnostic.PerformancePressure == PerformancePressureLevel.High)
        {
            return StreamingReadinessTone.Caution;
        }

        return diagnostic.AvailableMemoryGiB >= 8
            && diagnostic.PerformancePressure == PerformancePressureLevel.Low
            ? StreamingReadinessTone.Ready
            : StreamingReadinessTone.Neutral;
    }
}

public enum StreamingReadinessLevel
{
    NotDetected,
    Ready,
    Attention,
    Protected,
    Partial
}

public enum StreamingReadinessCheckKind
{
    Software,
    Resources,
    GameSession
}

public enum StreamingReadinessTone
{
    Neutral,
    Ready,
    Caution,
    Protected
}

public sealed record StreamingReadinessCheck(
    StreamingReadinessCheckKind Kind,
    StreamingReadinessTone Tone,
    IReadOnlyList<string> ApplicationNames);

public sealed record StreamingReadinessAssessment(
    StreamingReadinessLevel Level,
    IReadOnlyList<StreamingReadinessCheck> Checks,
    bool HasKnownSoftwareInstalled,
    bool HasKnownProcessRunning);
