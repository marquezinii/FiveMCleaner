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
    protected AllowlistedRegistryAction(IRegistryStore registry)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    protected IRegistryStore Registry { get; }

    protected abstract IReadOnlyList<RegistryMutation> GetMutations();

    protected virtual RegistryValueState? ResolveDesiredValue(
        RegistryMutation mutation,
        RegistryValueState previousValue)
    {
        return mutation.DesiredValue;
    }

    protected virtual bool IsAllowedRollbackEntry(
        RegistryAddress address,
        RegistryValueState previousValue,
        RegistryValueState appliedValue,
        IReadOnlyList<RegistryMutation> currentMutations)
    {
        var key = CanonicalAddress(address);
        var mutation = currentMutations.FirstOrDefault(candidate =>
            CanonicalAddress(candidate.Address).Equals(key, StringComparison.OrdinalIgnoreCase));
        return mutation is not null && Equivalent(appliedValue, mutation.DesiredValue);
    }

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
                var previous = Registry.Read(mutation.Address);
                if (mutation.RequireExistingValue && !previous.Exists)
                {
                    continue;
                }

                var desired = ResolveDesiredValue(mutation, previous);
                if (desired is null || Equivalent(previous, desired))
                {
                    continue;
                }

                Registry.Write(mutation.Address, desired);
                applied.Add(new RegistryMutationSnapshotEntry(
                    mutation.Address,
                    previous,
                    desired));
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
        ValidateRollbackSnapshot(snapshot);
        RestoreEntries(snapshot.Entries, requireAppliedValue: true);
        return Task.CompletedTask;
    }

    private void ValidateRollbackSnapshot(RegistryMutationSnapshot snapshot)
    {
        if (snapshot.Entries is null || snapshot.Entries.Count == 0)
        {
            throw new InvalidDataException(
                "O snapshot de registro não contém nenhuma alteração para restaurar.");
        }

        var currentMutations = GetMutations();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Entries)
        {
            var key = CanonicalAddress(entry.Address);
            if (!seen.Add(key)
                || !IsAllowedRollbackEntry(
                    entry.Address,
                    entry.PreviousValue,
                    entry.AppliedValue,
                    currentMutations))
            {
                throw new InvalidDataException(
                    "O snapshot de registro contém um endereço ou valor fora da allowlist desta ação.");
            }
        }
    }

    private static string CanonicalAddress(RegistryAddress address)
    {
        return $"{(int)address.Hive}|{address.SubKey.Trim('\\')}|{address.ValueName}";
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
                var current = Registry.Read(entry.Address);
                if (!Equivalent(current, entry.AppliedValue))
                {
                    conflicts.Add(entry.Address);
                    continue;
                }
            }

            if (entry.PreviousValue.Exists)
            {
                Registry.Write(entry.Address, entry.PreviousValue);
            }
            else
            {
                Registry.Delete(entry.Address);
            }
        }

        if (conflicts.Count > 0)
        {
            throw new IOException(
                $"Rollback recusou sobrescrever {conflicts.Count} valor(es) de registro alterado(s) depois da otimização.");
        }
    }

    protected static bool Equivalent(RegistryValueState left, RegistryValueState right)
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
                    @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                    "HistoricalCaptureEnabled"),
                disabled)
        ];
    }
}

public sealed class GpuPreferenceRegistryAction : AllowlistedRegistryAction
{
    private readonly IReadOnlyList<RegistryAddress> addresses;
    private readonly string fiveMExecutable;
    private readonly string fiveMRuntimeDirectory;

    public GpuPreferenceRegistryAction(
        IRegistryStore registry,
        string fiveMExecutablePath,
        string fiveMInstallationRoot)
        : base(registry)
    {
        fiveMExecutable = Path.GetFullPath(fiveMExecutablePath);
        if (!Path.GetExtension(fiveMExecutable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "GPU preference target must be an executable.",
                nameof(fiveMExecutablePath));
        }

        _ = SafePath.EnsureDescendant(fiveMInstallationRoot, fiveMExecutable);
        fiveMRuntimeDirectory = Path.Combine(
            SafePath.Normalize(fiveMInstallationRoot),
            "FiveM.app",
            "data",
            "cache",
            "subprocess");
        var targets = new List<string> { fiveMExecutable };
        AddFiveMRuntimeTargets(targets, fiveMInstallationRoot);
        addresses = targets
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(target => new RegistryAddress(
                RegistryHive.CurrentUser,
                @"Software\Microsoft\DirectX\UserGpuPreferences",
                target))
            .ToArray();
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.PreferHighPerformanceGpu);

    protected override IReadOnlyList<RegistryMutation> GetMutations()
    {
        return addresses
            .Select(address => new RegistryMutation(
                address,
                RegistryValueState.FromString("GpuPreference=2;")))
            .ToArray();
    }

    protected override RegistryValueState? ResolveDesiredValue(
        RegistryMutation mutation,
        RegistryValueState previousValue)
    {
        return BuildGpuPreferenceValue(previousValue) is { } desired
            ? RegistryValueState.FromString(desired)
            : null;
    }

    protected override bool IsAllowedRollbackEntry(
        RegistryAddress address,
        RegistryValueState previousValue,
        RegistryValueState appliedValue,
        IReadOnlyList<RegistryMutation> currentMutations)
    {
        if (address.Hive != RegistryHive.CurrentUser
            || !address.SubKey.Equals(
                @"Software\Microsoft\DirectX\UserGpuPreferences",
                StringComparison.OrdinalIgnoreCase)
            || BuildGpuPreferenceValue(previousValue) is not { } expectedAppliedValue
            || !Equivalent(
                appliedValue,
                RegistryValueState.FromString(expectedAppliedValue)))
        {
            return false;
        }

        string target;
        try
        {
            if (!Path.IsPathFullyQualified(address.ValueName))
            {
                return false;
            }

            target = Path.GetFullPath(address.ValueName);
            if (!target.Equals(address.ValueName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }

        if (target.Equals(fiveMExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = Path.GetFileName(target);
        var parent = Path.GetDirectoryName(target);
        if (parent?.Equals(fiveMRuntimeDirectory, StringComparison.OrdinalIgnoreCase) == true
            && IsKnownFiveMRendererName(fileName))
        {
            return true;
        }

        return false;
    }

    private static string? BuildGpuPreferenceValue(RegistryValueState current)
    {
        if (!current.Exists)
        {
            return "GpuPreference=2;";
        }

        if (current.Kind != RegistryValueKind.String
            || string.IsNullOrWhiteSpace(current.StringValue)
            || current.StringValue.IndexOfAny(['\r', '\n']) >= 0)
        {
            return null;
        }

        var output = new List<string>();
        var foundPreference = false;
        foreach (var rawSegment in current.StringValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            var separator = segment.IndexOf('=');
            if (separator <= 0
                || separator == segment.Length - 1
                || segment.IndexOf('=', separator + 1) >= 0)
            {
                return null;
            }

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (key.Length is 0 or > 64
                || value.Length is 0 or > 128
                || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            {
                return null;
            }

            if (key.Equals("GpuPreference", StringComparison.OrdinalIgnoreCase))
            {
                if (foundPreference)
                {
                    return null;
                }

                output.Add("GpuPreference=2");
                foundPreference = true;
            }
            else
            {
                output.Add($"{key}={value}");
            }
        }

        if (!foundPreference)
        {
            output.Add("GpuPreference=2");
        }

        return string.Join(';', output) + ";";
    }

    private static void AddFiveMRuntimeTargets(ICollection<string> targets, string installationRoot)
    {
        var normalizedRoot = SafePath.Normalize(installationRoot);
        var searchRoot = Path.Combine(
            normalizedRoot,
            "FiveM.app",
            "data",
            "cache",
            "subprocess");
        try
        {
            if (!Directory.Exists(searchRoot)
                || HasReparsePointInRuntimePath(normalizedRoot, searchRoot))
            {
                return;
            }

            foreach (var candidate in Directory
                         .EnumerateFiles(searchRoot, "FiveM*_GTAProcess.exe", SearchOption.TopDirectoryOnly)
                         .Take(64))
            {
                if ((new FileInfo(candidate).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var fileName = Path.GetFileName(candidate);
                if (IsKnownFiveMRendererName(fileName))
                {
                    targets.Add(SafePath.EnsureDescendant(installationRoot, candidate));
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // The stable FiveM launcher target is still applied when runtime discovery is unavailable.
        }
    }

    private static bool IsKnownFiveMRendererName(string fileName)
    {
        if (fileName.Equals("FiveM_GTAProcess.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.StartsWith("FiveM_b", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith("_GTAProcess.exe", StringComparison.OrdinalIgnoreCase)
            && fileName[7..^15].Length > 0
            && fileName[7..^15].All(char.IsAsciiDigit);
    }

    private static bool HasReparsePointInRuntimePath(string installationRoot, string searchRoot)
    {
        var current = new DirectoryInfo(searchRoot);
        var normalizedInstallationRoot = SafePath.Normalize(installationRoot);
        while (current is not null
               && current.FullName.StartsWith(
                   normalizedInstallationRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}
