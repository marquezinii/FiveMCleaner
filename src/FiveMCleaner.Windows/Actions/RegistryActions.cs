using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;
using Microsoft.Win32;

namespace FiveMCleaner.Windows.Actions;

public sealed record RegistryMutation(
    RegistryAddress Address,
    RegistryValueState DesiredValue,
    bool RequireExistingValue = false);

internal sealed record RegistryMutationSnapshotEntry(
    RegistryAddress Address,
    RegistryValueState PreviousValue,
    RegistryValueState AppliedValue);

internal sealed record RegistryMutationSnapshot(
    IReadOnlyList<RegistryMutationSnapshotEntry> Entries);

public abstract class AllowlistedRegistryAction : WindowsOptimizationAction
{
    private readonly IRegistryStore registry;

    protected AllowlistedRegistryAction(IRegistryStore registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    protected abstract IReadOnlyList<RegistryMutation> GetMutations();

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        var applied = new List<RegistryMutationSnapshotEntry>();
        try
        {
            foreach (var mutation in GetMutations())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = registry.Read(mutation.Address);
                if (mutation.RequireExistingValue && !previous.Exists)
                {
                    continue;
                }

                if (Equivalent(previous, mutation.DesiredValue))
                {
                    continue;
                }

                registry.Write(mutation.Address, mutation.DesiredValue);
                applied.Add(new RegistryMutationSnapshotEntry(
                    mutation.Address,
                    previous,
                    mutation.DesiredValue));
            }
        }
        catch
        {
            RestoreEntries(applied, requireAppliedValue: false);
            throw;
        }

        if (applied.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum valor compatível precisou ser alterado."));
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new RegistryMutationSnapshot(applied),
            $"{applied.Count} configuração(ões) allowlisted atualizada(s)."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<RegistryMutationSnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        RestoreEntries(snapshot.Entries, requireAppliedValue: true);
        return Task.CompletedTask;
    }

    private void RestoreEntries(
        IEnumerable<RegistryMutationSnapshotEntry> entries,
        bool requireAppliedValue)
    {
        var conflicts = new List<RegistryAddress>();
        foreach (var entry in entries.Reverse())
        {
            if (requireAppliedValue)
            {
                var current = registry.Read(entry.Address);
                if (!Equivalent(current, entry.AppliedValue))
                {
                    conflicts.Add(entry.Address);
                    continue;
                }
            }

            if (entry.PreviousValue.Exists)
            {
                registry.Write(entry.Address, entry.PreviousValue);
            }
            else
            {
                registry.Delete(entry.Address);
            }
        }

        if (conflicts.Count > 0)
        {
            throw new IOException(
                $"Rollback recusou sobrescrever {conflicts.Count} valor(es) de registro alterado(s) depois da otimização.");
        }
    }

    private static bool Equivalent(RegistryValueState left, RegistryValueState right)
    {
        return left.Exists == right.Exists
            && left.Kind == right.Kind
            && string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal)
            && left.NumericValue == right.NumericValue
            && string.Equals(left.BinaryBase64Value, right.BinaryBase64Value, StringComparison.Ordinal)
            && SequenceEqual(left.MultiStringValue, right.MultiStringValue);
    }

    private static bool SequenceEqual(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && left.SequenceEqual(right, StringComparer.Ordinal);
    }
}

public sealed class GameModeRegistryAction : AllowlistedRegistryAction
{
    private static readonly RegistryAddress Address = new(
        RegistryHive.CurrentUser,
        @"Software\Microsoft\GameBar",
        "AutoGameModeEnabled");

    public GameModeRegistryAction(IRegistryStore registry)
        : base(registry)
    {
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.EnableGameMode);

    protected override IReadOnlyList<RegistryMutation> GetMutations()
    {
        return [new RegistryMutation(Address, RegistryValueState.FromDword(1))];
    }
}

public sealed class GameDvrRegistryAction : AllowlistedRegistryAction
{
    public GameDvrRegistryAction(IRegistryStore registry)
        : base(registry)
    {
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DisableBackgroundCapture);

    protected override IReadOnlyList<RegistryMutation> GetMutations()
    {
        var disabled = RegistryValueState.FromDword(0);
        return
        [
            new RegistryMutation(
                new RegistryAddress(
                    RegistryHive.CurrentUser,
                    @"System\GameConfigStore",
                    "GameDVR_Enabled"),
                disabled),
            new RegistryMutation(
                new RegistryAddress(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                    "AppCaptureEnabled"),
                disabled)
        ];
    }
}

public sealed class GpuPreferenceRegistryAction : AllowlistedRegistryAction
{
    private readonly RegistryAddress address;

    public GpuPreferenceRegistryAction(
        IRegistryStore registry,
        string fiveMExecutablePath,
        string fiveMInstallationRoot)
        : base(registry)
    {
        var executable = Path.GetFullPath(fiveMExecutablePath);
        if (!Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "GPU preference target must be an executable.",
                nameof(fiveMExecutablePath));
        }

        _ = SafePath.EnsureDescendant(fiveMInstallationRoot, executable);
        address = new RegistryAddress(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\DirectX\UserGpuPreferences",
            executable);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.PreferHighPerformanceGpu);

    protected override IReadOnlyList<RegistryMutation> GetMutations()
    {
        return
        [
            new RegistryMutation(
                address,
                RegistryValueState.FromString("GpuPreference=2;"))
        ];
    }
}
