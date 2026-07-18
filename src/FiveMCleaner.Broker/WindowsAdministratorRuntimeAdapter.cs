using FiveMCleaner.Contracts;
using FiveMCleaner.Windows;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Engine;

namespace FiveMCleaner.Broker;

internal sealed class WindowsAdministratorRuntimeAdapter
{
    private readonly WindowsOptimizationRuntime runtime;

    private WindowsAdministratorRuntimeAdapter(WindowsOptimizationRuntime runtime)
    {
        this.runtime = runtime;
    }

    public static WindowsAdministratorRuntimeAdapter CreateDefault()
    {
        return new WindowsAdministratorRuntimeAdapter(WindowsOptimizationRuntime.CreateDefault());
    }

    public Task<WindowsTransactionResult> ExecuteAsync(
        ValidatedPlan validatedPlan,
        NamedPipeEventWriter events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validatedPlan);
        ArgumentNullException.ThrowIfNull(events);

        var actions = runtime.ResolveAdministratorActions(validatedPlan.Plan);
        ValidateResolvedActions(actions, validatedPlan.AdministratorActions);

        var context = new WindowsActionContext
        {
            TransactionId = validatedPlan.Plan.PlanId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = true,
            Progress = new InlineProgress<WindowsActionProgress>(progress =>
                events.Publish(
                    BrokerEventKind.Progress,
                    progress.Message,
                    item =>
                    {
                        item.TransactionId = progress.TransactionId;
                        item.ActionId = progress.ActionId;
                        item.CompletedWeight = progress.CompletedWeight;
                        item.TotalWeight = progress.TotalWeight;
                    }))
        };

        return runtime.Engine.ExecuteAsync(
            actions,
            context,
            new WindowsTransactionOptions
            {
                IncludeStandardUserActions = false,
                IncludeAdministratorActions = true,
                RollbackOnFailure = true
            },
            cancellationToken);
    }

    public Task<WindowsTransactionResult> RollbackAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction ID cannot be empty.", nameof(transactionId));
        }

        return runtime.Engine.RollbackAsync(
            transactionId,
            isElevated: true,
            new WindowsRollbackOptions
            {
                IncludeStandardUserActions = false,
                IncludeAdministratorActions = true
            },
            cancellationToken);
    }

    private static void ValidateResolvedActions(
        IReadOnlyList<IWindowsOptimizationAction> resolved,
        IReadOnlyList<PlannedActionDto> expected)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (resolved.Count != expected.Count || resolved.Count == 0)
        {
            throw new InvalidOperationException(
                "The Windows factory did not resolve the exact administrator action set.");
        }

        for (var index = 0; index < resolved.Count; index++)
        {
            var actual = resolved[index].Metadata;
            var planned = expected[index].Metadata;
            if (actual.RequiredPrivilege != RequiredPrivilege.Administrator
                || !string.Equals(actual.Id, planned.Id, StringComparison.Ordinal)
                || actual.Version != planned.Version)
            {
                throw new InvalidOperationException(
                    "The Windows factory returned an action outside the validated administrator plan.");
            }
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> callback;

        public InlineProgress(Action<T> callback)
        {
            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Report(T value)
        {
            callback(value);
        }
    }
}
