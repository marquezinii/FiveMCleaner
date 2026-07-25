using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public interface IAppOptimizationService
{
    string LogsDirectory { get; }

    Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default);

    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a settings file already exists on disk, without deserializing
    /// it. Used only to distinguish a brand-new installation from an older
    /// installation that already had settings but never confirmed a
    /// versioned privacy consent — see <see cref="PrivacyConsentEvaluator"/>.
    /// </summary>
    bool SettingsFileExists();

    Task<AppOptimizationResult> ExecuteAsync(
        OptimizationPlanDto plan,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppHistoryRecord>> LoadHistoryAsync(
        CancellationToken cancellationToken = default);

    Task<bool> RollbackAsync(
        Guid transactionId,
        IProgress<AppProgressUpdate> progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches the official GTA V standalone benchmark (never inside a
    /// FiveM session) and reads back the game's own frame time output.
    /// Requires GTA V to be closed first; explicit, opt-in, never part of
    /// the automatic optimization flow.
    /// </summary>
    Task<AppGtaVBenchmarkResult> RunGtaVBenchmarkAsync(
        int iterations,
        CancellationToken cancellationToken = default);
}
