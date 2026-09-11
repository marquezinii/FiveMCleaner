using System.Text.Json;
using Ralven.Contracts;
using Ralven.Core.Catalog;

namespace Ralven.Windows.Actions;

public static class WindowsActionMetadata
{
    public static ActionMetadataDto For(string actionId)
    {
        return ActionCatalog.Current.GetRequired(actionId).ToMetadata();
    }

    internal static bool MatchesCore(ActionMetadataDto metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!ActionCatalog.Current.TryGet(metadata.Id, out var definition))
        {
            return false;
        }

        return metadata.MatchesExactly(definition!.ToMetadata());
    }
}

public sealed record WindowsActionProgress(
    Guid TransactionId,
    string ActionId,
    string Message,
    int CompletedWeight,
    int TotalWeight,
    int CompletedSteps = 0,
    int TotalSteps = 0,
    ActionExecutionOutcome Outcome = ActionExecutionOutcome.Pending);

public sealed record WindowsActionContext
{
    public required Guid TransactionId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required bool IsElevated { get; init; }

    /// <summary>The profile that created the transaction, when execution came from a plan.</summary>
    public OptimizationProfile? Profile { get; init; }

    public PersonalUsage? PersonalUsage { get; init; }

    /// <summary>
    /// True only while the engine is restoring snapshots created by the same
    /// execution after that execution failed or was cancelled. User-requested
    /// rollback never sets this flag.
    /// </summary>
    public bool IsImmediateFailureRecovery { get; internal init; }

    public IProgress<WindowsActionProgress>? Progress { get; init; }
}

public sealed record WindowsActionApplyResult
{
    public required bool Changed { get; init; }

    /// <summary>
    /// Semantic result when no write was needed. A missing precondition is
    /// different from a verified desired state and remains visible in reports.
    /// </summary>
    public ActionExecutionOutcome Outcome { get; init; } = ActionExecutionOutcome.Verified;

    public string? SnapshotJson { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public static WindowsActionApplyResult NoChange(params string[] messages)
    {
        return new WindowsActionApplyResult
        {
            Changed = false,
            Outcome = ActionExecutionOutcome.Verified,
            Messages = messages
        };
    }

    public static WindowsActionApplyResult Skipped(params string[] messages)
    {
        return new WindowsActionApplyResult
        {
            Changed = false,
            Outcome = ActionExecutionOutcome.Skipped,
            Messages = messages
        };
    }

    public static WindowsActionApplyResult ChangedWith<TSnapshot>(
        TSnapshot snapshot,
        params string[] messages)
    {
        return new WindowsActionApplyResult
        {
            Changed = true,
            Outcome = ActionExecutionOutcome.Applied,
            SnapshotJson = WindowsActionSnapshot.Serialize(snapshot),
            Messages = messages
        };
    }
}

public interface IWindowsOptimizationAction
{
    ActionMetadataDto Metadata { get; }

    Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken);

    Task CommitAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken);

    Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken);
}

public abstract class WindowsOptimizationAction : IWindowsOptimizationAction
{
    public abstract ActionMetadataDto Metadata { get; }

    public abstract Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken);

    public virtual Task CommitAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public abstract Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken);
}

public static class WindowsActionSnapshot
{
    public static string Serialize<T>(T snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, RalvenJson.Options);
    }

    public static T Deserialize<T>(string? json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<T>(json, RalvenJson.Options)
            ?? throw new JsonException($"Snapshot did not contain {typeof(T).Name}.");
    }
}

public sealed class WindowsActionCatalog
{
    private readonly Dictionary<string, IWindowsOptimizationAction> actions;

    public WindowsActionCatalog(IEnumerable<IWindowsOptimizationAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        this.actions = new Dictionary<string, IWindowsOptimizationAction>(StringComparer.Ordinal);

        foreach (var action in actions)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (!WindowsActionMetadata.MatchesCore(action.Metadata))
            {
                throw new ArgumentException(
                    $"Action '{action.Metadata.Id}' does not exactly match the current Core allowlist.",
                    nameof(actions));
            }

            if (action.Metadata.Version <= 0)
            {
                throw new ArgumentException(
                    $"Action '{action.Metadata.Id}' has an invalid version.",
                    nameof(actions));
            }

            if (!this.actions.TryAdd(action.Metadata.Id, action))
            {
                throw new ArgumentException(
                    $"Action '{action.Metadata.Id}' was registered more than once.",
                    nameof(actions));
            }
        }
    }

    public IReadOnlyCollection<IWindowsOptimizationAction> Actions => actions.Values;

    public IWindowsOptimizationAction GetRequired(string id, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (!actions.TryGetValue(id, out var action))
        {
            throw new KeyNotFoundException($"Action '{id}' is not registered in this catalog.");
        }

        if (action.Metadata.Version != version)
        {
            throw new InvalidOperationException(
                $"Action '{id}' journal version {version} does not match handler version {action.Metadata.Version}.");
        }

        return action;
    }

    public void Validate(IWindowsOptimizationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var registered = GetRequired(action.Metadata.Id, action.Metadata.Version);

        if (registered.GetType() != action.GetType())
        {
            throw new InvalidOperationException(
                $"Action '{action.Metadata.Id}' is allowlisted, but handler type '{action.GetType().Name}' is not registered.");
        }
    }
}
