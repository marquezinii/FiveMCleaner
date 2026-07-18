using FiveMCleaner.Contracts;

namespace FiveMCleaner.App.Services;

public interface IAppOptimizationService
{
    string LogsDirectory { get; }

    Task<AppDiagnostic> DiagnoseAsync(CancellationToken cancellationToken = default);

    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

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
}
