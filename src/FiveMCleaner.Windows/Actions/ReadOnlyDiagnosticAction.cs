namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Base for the read-only diagnostics. They never change the machine: applying
/// one always resolves to <see cref="WindowsActionApplyResult.NoChange"/> with
/// a single informative message, there is no snapshot to keep and rollback is a
/// no-op. Derived actions only produce the message and must degrade a signal
/// they could not read into an honest "not available" text -- a diagnostic that
/// throws would abort a run it was never allowed to change.
/// </summary>
public abstract class ReadOnlyDiagnosticAction : WindowsOptimizationAction
{
    public sealed override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Describe()));
    }

    public sealed override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Builds the message reported to the user for this diagnostic.</summary>
    protected abstract string Describe();
}
