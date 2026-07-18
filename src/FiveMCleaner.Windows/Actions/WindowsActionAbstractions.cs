using System.Text.Json;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;

namespace FiveMCleaner.Windows.Actions;

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

        var expected = definition!.ToMetadata();
        return metadata.Id == expected.Id
            && metadata.Version == expected.Version
            && metadata.Name == expected.Name
            && metadata.Description == expected.Description
            && metadata.Category == expected.Category
            && metadata.SupportedProfiles.SequenceEqual(expected.SupportedProfiles)
            && metadata.Risk == expected.Risk
            && metadata.Reversibility == expected.Reversibility
            && metadata.RequiredPrivilege == expected.RequiredPrivilege
            && metadata.RequiresFiveMStopped == expected.RequiresFiveMStopped
            && metadata.RequiresAcPower == expected.RequiresAcPower
            && metadata.RequiresRestart == expected.RequiresRestart
            && metadata.ProgressWeight == expected.ProgressWeight
            && metadata.ExpectedImpact == expected.ExpectedImpact;
    }
}

public sealed record WindowsActionProgress(
    Guid TransactionId,
    string ActionId,
    string Message,
    int CompletedWeight,
    int TotalWeight);

public sealed record WindowsActionContext
{
    public required Guid TransactionId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required bool IsElevated { get; init; }

    public IProgress<WindowsActionProgress>? Progress { get; init; }
}

public sealed record WindowsActionApplyResult
{
    public required bool Changed { get; init; }

    public string? SnapshotJson { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public static WindowsActionApplyResult NoChange(params string[] messages)
    {
        return new WindowsActionApplyResult
        {
            Changed = false,
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
        return JsonSerializer.Serialize(snapshot, FiveMCleanerJson.Options);
    }

    public static T Deserialize<T>(string? json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<T>(json, FiveMCleanerJson.Options)
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

    public bool TryGet(string id, int version, out IWindowsOptimizationAction? action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (actions.TryGetValue(id, out var found) && found.Metadata.Version == version)
        {
            action = found;
            return true;
        }

        action = null;
        return false;
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
