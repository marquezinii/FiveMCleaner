using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

public sealed record CleanupScope(
    string Name,
    string Root,
    DateTimeOffset CutoffUtc);

internal sealed record QuarantinedFileSnapshot(
    string OriginalPath,
    string QuarantinePath,
    long Length);

internal sealed record CleanupScopeSnapshot(
    string Name,
    string Root,
    string QuarantineRoot,
    IReadOnlyList<QuarantinedFileSnapshot> Files,
    IReadOnlyList<string> SkippedReparsePoints,
    IReadOnlyList<string> SkippedInaccessiblePaths);

internal sealed record CleanupActionSnapshot(
    IReadOnlyList<CleanupScopeSnapshot> Scopes);

public sealed class VerifyFiveMStoppedAction : WindowsOptimizationAction
{
    private readonly string installationRoot;
    private readonly IFiveMProcessInspector processInspector;

    public VerifyFiveMStoppedAction(
        string installationRoot,
        IFiveMProcessInspector processInspector)
    {
        this.installationRoot = SafePath.Normalize(installationRoot);
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.VerifyFiveMIsStopped);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (processInspector.IsRunningFrom(installationRoot))
        {
            throw new InvalidOperationException(
                "O FiveM está em execução. Feche-o antes de iniciar a otimização.");
        }

        return Task.FromResult(WindowsActionApplyResult.NoChange(
            "FiveM fechado; é seguro continuar."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class VerifyGtaVStoppedAction : WindowsOptimizationAction
{
    private readonly string? installationRoot;
    private readonly IGtaVProcessInspector processInspector;

    public VerifyGtaVStoppedAction(
        string? installationRoot,
        IGtaVProcessInspector processInspector)
    {
        this.installationRoot = string.IsNullOrWhiteSpace(installationRoot)
            ? null
            : SafePath.Normalize(installationRoot);
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.VerifyGtaVIsStopped);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (processInspector.IsRunningFrom(installationRoot))
        {
            throw new InvalidOperationException(
                "O GTA V está em execução. Feche-o antes de iniciar a otimização.");
        }

        return Task.FromResult(WindowsActionApplyResult.NoChange(
            "GTA V fechado; é seguro continuar."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class UserTemporaryFilesCleanupAction : QuarantineCleanupAction
{
    private readonly string temporaryDirectory;
    private readonly TimeSpan minimumAge;

    public UserTemporaryFilesCleanupAction(
        string temporaryDirectory,
        TimeSpan minimumAge,
        SafeFileTree? fileTree = null)
        : base(fileTree)
    {
        temporaryDirectory = SafePath.Normalize(temporaryDirectory);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (systemRoot is not null
            && temporaryDirectory.Equals(
                Path.TrimEndingDirectorySeparator(systemRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The temporary directory cannot be a drive root.", nameof(temporaryDirectory));
        }

        this.temporaryDirectory = temporaryDirectory;
        this.minimumAge = minimumAge > TimeSpan.Zero
            ? minimumAge
            : throw new ArgumentOutOfRangeException(nameof(minimumAge));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.CleanUserTemporaryFiles);

    protected override IReadOnlyList<CleanupScope> GetScopes(WindowsActionContext context)
    {
        return
        [
            new CleanupScope(
                "user-temp",
                temporaryDirectory,
                context.StartedAtUtc - minimumAge)
        ];
    }
}

public sealed class LegacyCrashDumpsPruneAction : QuarantineCleanupAction
{
    private readonly string fiveMAppRoot;
    private readonly string installationRoot;
    private readonly TimeSpan retention;
    private readonly IFiveMProcessInspector processInspector;

    public LegacyCrashDumpsPruneAction(
        string fiveMAppRoot,
        string installationRoot,
        TimeSpan retention,
        IFiveMProcessInspector processInspector,
        SafeFileTree? fileTree = null)
        : base(fileTree)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
        this.installationRoot = SafePath.Normalize(installationRoot);
        _ = SafePath.EnsureDescendant(this.installationRoot, this.fiveMAppRoot);
        this.retention = retention > TimeSpan.Zero
            ? retention
            : throw new ArgumentOutOfRangeException(nameof(retention));
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.PruneLegacyCrashDumps);

    protected override IReadOnlyList<CleanupScope> GetScopes(WindowsActionContext context)
    {
        EnsureFiveMStopped();
        var cutoff = context.StartedAtUtc - retention;
        return
        [
            new CleanupScope(
                "logs",
                SafePath.EnsureDescendant(fiveMAppRoot, Path.Combine(fiveMAppRoot, "logs")),
                cutoff),
            new CleanupScope(
                "crashes",
                SafePath.EnsureDescendant(fiveMAppRoot, Path.Combine(fiveMAppRoot, "crashes")),
                cutoff)
        ];
    }

    private void EnsureFiveMStopped()
    {
        if (processInspector.IsRunningFrom(installationRoot))
        {
            throw new InvalidOperationException("FiveM precisa estar fechado para limpar diagnósticos.");
        }
    }
}

public sealed class LegacyServerCacheRepairAction : QuarantineCleanupAction
{
    private readonly string fiveMAppRoot;
    private readonly string installationRoot;
    private readonly CacheRepairPolicy policy;
    private readonly long thresholdBytes;
    private readonly IFiveMProcessInspector processInspector;
    private readonly SafeFileTree fileTree;

    public LegacyServerCacheRepairAction(
        string fiveMAppRoot,
        string installationRoot,
        CacheRepairPolicy policy,
        long thresholdBytes,
        IFiveMProcessInspector processInspector,
        SafeFileTree? fileTree = null)
        : base(fileTree)
    {
        if (policy == CacheRepairPolicy.Off || !Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        if (thresholdBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdBytes));
        }

        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
        this.installationRoot = SafePath.Normalize(installationRoot);
        _ = SafePath.EnsureDescendant(this.installationRoot, this.fiveMAppRoot);
        this.policy = policy;
        this.thresholdBytes = thresholdBytes;
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
        this.fileTree = fileTree ?? new SafeFileTree();
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.RepairLegacyServerCache);

    protected override IReadOnlyList<CleanupScope> GetScopes(WindowsActionContext context)
    {
        if (processInspector.IsRunningFrom(installationRoot))
        {
            throw new InvalidOperationException("FiveM precisa estar fechado para reparar o cache.");
        }

        var dataRoot = SafePath.EnsureDescendant(
            fiveMAppRoot,
            Path.Combine(fiveMAppRoot, "data"));
        var roots = new[]
        {
            SafePath.EnsureDescendant(dataRoot, Path.Combine(dataRoot, "server-cache")),
            SafePath.EnsureDescendant(dataRoot, Path.Combine(dataRoot, "server-cache-priv"))
        };

        if (policy == CacheRepairPolicy.WhenOversized)
        {
            var currentSize = roots
                .Where(Directory.Exists)
                .Select(root => fileTree.EnumerateFiles(root, _ => true))
                .SelectMany(result => result.Files)
                .Sum(file => file.Length);
            if (currentSize < thresholdBytes)
            {
                return [];
            }
        }

        var allExistingFiles = context.StartedAtUtc.AddMinutes(1);
        return
        [
            new CleanupScope("server-cache", roots[0], allExistingFiles),
            new CleanupScope("server-cache-priv", roots[1], allExistingFiles)
        ];
    }
}

public abstract class QuarantineCleanupAction : WindowsOptimizationAction
{
    private const string QuarantineDirectoryName = ".fivemcleaner-quarantine";
    private readonly SafeFileTree fileTree;

    protected QuarantineCleanupAction(SafeFileTree? fileTree)
    {
        this.fileTree = fileTree ?? new SafeFileTree();
    }

    protected abstract IReadOnlyList<CleanupScope> GetScopes(WindowsActionContext context);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<CleanupScopeSnapshot>();
        try
        {
            foreach (var scope in GetScopes(context))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(scope.Root))
                {
                    continue;
                }

                var enumeration = fileTree.EnumerateFiles(
                    scope.Root,
                    entry => entry.LastWriteTimeUtc <= scope.CutoffUtc,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        QuarantineDirectoryName
                    });
                var quarantineRoot = SafePath.EnsureDescendant(
                    scope.Root,
                    Path.Combine(
                        scope.Root,
                        QuarantineDirectoryName,
                        context.TransactionId.ToString("N"),
                        scope.Name));
                var moved = new List<QuarantinedFileSnapshot>();

                foreach (var file in enumeration.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _ = SafePath.EnsureDescendant(scope.Root, file.FullPath);
                    SafePath.EnsureNoReparsePoints(file.FullPath);
                    var destination = SafePath.EnsureDescendant(
                        quarantineRoot,
                        Path.Combine(quarantineRoot, file.RelativePath));
                    try
                    {
                        var destinationDirectory = Path.GetDirectoryName(destination)!;
                        Directory.CreateDirectory(destinationDirectory);
                        SafePath.EnsureNoReparsePoints(destinationDirectory);
                        File.Move(file.FullPath, destination, overwrite: false);
                        moved.Add(new QuarantinedFileSnapshot(
                            file.FullPath,
                            destination,
                            file.Length));
                    }
                    catch (Exception exception) when (exception is IOException
                        or UnauthorizedAccessException)
                    {
                        // Locked or protected files are intentionally preserved.
                    }
                }

                snapshots.Add(new CleanupScopeSnapshot(
                    scope.Name,
                    scope.Root,
                    quarantineRoot,
                    moved,
                    enumeration.SkippedReparsePoints,
                    enumeration.SkippedInaccessiblePaths));
            }
        }
        catch
        {
            RestoreMovedFiles(snapshots, throwOnConflict: false);
            throw;
        }

        var fileCount = snapshots.Sum(snapshot => snapshot.Files.Count);
        var byteCount = snapshots.Sum(snapshot => snapshot.Files.Sum(file => file.Length));
        if (fileCount == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum arquivo allowlisted atendia aos critérios da limpeza."));
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new CleanupActionSnapshot(snapshots),
            $"{fileCount} arquivo(s) preparado(s) para limpeza ({byteCount:N0} bytes)."));
    }

    public override Task CommitAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return Task.CompletedTask;
        }

        var snapshot = WindowsActionSnapshot.Deserialize<CleanupActionSnapshot>(snapshotJson);
        foreach (var scope in snapshot.Scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(scope.QuarantineRoot))
            {
                fileTree.PurgeCreatedTree(scope.QuarantineRoot);
                var transactionDirectory = Path.GetDirectoryName(scope.QuarantineRoot);
                if (transactionDirectory is not null && Directory.Exists(transactionDirectory))
                {
                    fileTree.DeleteEmptyDirectoriesBottomUp(transactionDirectory, deleteRoot: true);
                }
            }

        }

        return Task.CompletedTask;
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return Task.CompletedTask;
        }

        var snapshot = WindowsActionSnapshot.Deserialize<CleanupActionSnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        RestoreMovedFiles(snapshot.Scopes, throwOnConflict: true);
        return Task.CompletedTask;
    }

    private static void RestoreMovedFiles(
        IEnumerable<CleanupScopeSnapshot> scopes,
        bool throwOnConflict)
    {
        var conflicts = new List<string>();
        foreach (var scope in scopes.Reverse())
        {
            SafePath.EnsureNoReparsePoints(scope.Root);
            _ = SafePath.EnsureDescendant(scope.Root, scope.QuarantineRoot);
            foreach (var file in scope.Files.Reverse())
            {
                _ = SafePath.EnsureDescendant(scope.Root, file.OriginalPath);
                _ = SafePath.EnsureDescendant(scope.QuarantineRoot, file.QuarantinePath);
                if (!File.Exists(file.QuarantinePath))
                {
                    continue;
                }

                if (File.Exists(file.OriginalPath))
                {
                    conflicts.Add(file.OriginalPath);
                    continue;
                }

                SafePath.EnsureNoReparsePoints(file.QuarantinePath);
                var originalDirectory = Path.GetDirectoryName(file.OriginalPath)!;
                Directory.CreateDirectory(originalDirectory);
                SafePath.EnsureNoReparsePoints(originalDirectory);
                File.Move(file.QuarantinePath, file.OriginalPath, overwrite: false);
            }
        }

        if (throwOnConflict && conflicts.Count > 0)
        {
            throw new IOException(
                $"Rollback preservou {conflicts.Count} arquivo(s) em quarentena porque os caminhos originais foram recriados.");
        }
    }
}
