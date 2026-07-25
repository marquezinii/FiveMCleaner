using System.Text.RegularExpressions;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Shared, best-effort text helpers for the small set of known FiveM crash
/// codes and log keywords used by the read-only diagnostics below. These are
/// heuristics over already-local text, never a memory dump analyzer.
/// </summary>
internal static class FiveMLogPatterns
{
    private const long MaxTailBytes = 512 * 1024;
    private static readonly Regex CrashCodePattern = new(
        @"0x[0-9A-Fa-f]{8}",
        RegexOptions.Compiled);

    private static readonly string[] StreamingKeywords =
    [
        "streaming", "ymap", "ytd", "ydr", "ybn", "resource start error", "failed to load"
    ];

    private static readonly string[] EntitlementFailureKeywords =
    [
        "entitlement", "ros_id", "social club", "authentication failed", "digitalentitlements"
    ];

    public static string? ReadLatestLogTail(string fiveMAppRoot)
    {
        var logsDirectory = Path.Combine(fiveMAppRoot, "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return null;
        }

        FileInfo? latest;
        try
        {
            latest = new DirectoryInfo(logsDirectory)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        if (latest is null)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                latest.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxTailBytes)
            {
                stream.Seek(-MaxTailBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyDictionary<string, int> CountRecurringCrashCodes(
        IEnumerable<string> dumpFileNames)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in dumpFileNames)
        {
            foreach (Match match in CrashCodePattern.Matches(fileName))
            {
                counts[match.Value] = counts.GetValueOrDefault(match.Value) + 1;
            }
        }

        return counts
            .Where(pair => pair.Value >= 2)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> FindStreamingErrorKeywords(string logTail)
    {
        return StreamingKeywords
            .Where(keyword => logTail.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static bool ContainsEntitlementFailurePattern(string logTail)
    {
        return EntitlementFailureKeywords.Any(
            keyword => logTail.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            && (logTail.Contains("error", StringComparison.OrdinalIgnoreCase)
                || logTail.Contains("fail", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class CacheStorageDiagnosisAction : WindowsOptimizationAction
{
    private const int MaxLockCheckPerScope = 200;
    private readonly string fiveMAppRoot;

    public CacheStorageDiagnosisAction(string fiveMAppRoot)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseCacheStorage);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataRoot = Path.Combine(fiveMAppRoot, "data");
        var scopes = new (string Name, string Path)[]
        {
            ("server-cache", Path.Combine(dataRoot, "server-cache")),
            ("server-cache-priv", Path.Combine(dataRoot, "server-cache-priv")),
            ("nui-storage", Path.Combine(dataRoot, "nui-storage")),
            ("logs", Path.Combine(fiveMAppRoot, "logs")),
            ("crashes", Path.Combine(fiveMAppRoot, "crashes"))
        };

        var summaries = new List<string>();
        var lockedFiles = 0;
        long totalBytes = 0;

        foreach (var scope in scopes)
        {
            if (!Directory.Exists(scope.Path))
            {
                continue;
            }

            long scopeBytes = 0;
            var checkedForLock = 0;
            try
            {
                foreach (var file in new DirectoryInfo(scope.Path)
                             .EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    scopeBytes += file.Length;
                    if (checkedForLock >= MaxLockCheckPerScope)
                    {
                        continue;
                    }

                    checkedForLock++;
                    if (IsLocked(file.FullName))
                    {
                        lockedFiles++;
                    }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                summaries.Add($"{scope.Name}: não foi possível ler completamente ({exception.Message}).");
                continue;
            }

            totalBytes += scopeBytes;
            summaries.Add($"{scope.Name}: {FormatBytes(scopeBytes)}");
        }

        if (summaries.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhuma pasta de cache ou dados do FiveM foi encontrada ainda."));
        }

        var message = $"Cache total: {FormatBytes(totalBytes)} ({string.Join(", ", summaries)})."
            + (lockedFiles > 0
                ? $" {lockedFiles} arquivo(s) parecem bloqueados por outro processo no momento da leitura."
                : " Nenhum arquivo bloqueado foi encontrado na amostra verificada.");
        return Task.FromResult(WindowsActionApplyResult.NoChange(message));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double mib = 1024d * 1024d;
        const double gib = mib * 1024d;
        return bytes >= gib
            ? $"{bytes / gib:0.##} GB"
            : $"{bytes / mib:0.#} MB";
    }
}

public sealed class InstallationHealthDiagnosisAction : WindowsOptimizationAction
{
    private const long MinimumFreeSpaceGiB = 5;
    private const long GiB = 1024L * 1024L * 1024L;
    private readonly string fiveMInstallationRoot;
    private readonly string fiveMAppRoot;

    public InstallationHealthDiagnosisAction(string fiveMInstallationRoot, string fiveMAppRoot)
    {
        this.fiveMInstallationRoot = SafePath.Normalize(fiveMInstallationRoot);
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseInstallationHealth);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<string>();

        if (TryFindDuplicateInstallation(out var duplicatePath))
        {
            findings.Add($"Possível instalação duplicada encontrada em '{duplicatePath}'.");
        }

        if (!HasWritePermission())
        {
            findings.Add("A pasta de dados do FiveM não aceitou escrita de teste; verifique permissões da pasta.");
        }

        if (IsUnderOneDrive())
        {
            findings.Add("A instalação está dentro de uma pasta sincronizada pelo OneDrive, o que pode causar bloqueios de arquivo durante o jogo.");
        }

        if (TryGetLowFreeSpace(out var freeGiB))
        {
            findings.Add($"Pouco espaço livre na unidade da instalação (~{freeGiB:0.#} GB).");
        }

        var message = findings.Count == 0
            ? "Nenhum problema de instalação foi encontrado nas verificações disponíveis."
            : string.Join(" ", findings);
        return Task.FromResult(WindowsActionApplyResult.NoChange(message));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private bool TryFindDuplicateInstallation(out string duplicatePath)
    {
        duplicatePath = string.Empty;
        var parent = Path.GetDirectoryName(fiveMInstallationRoot);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return false;
        }

        try
        {
            foreach (var candidate in Directory.EnumerateDirectories(parent, "FiveM*"))
            {
                if (candidate.Equals(fiveMInstallationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(Path.Combine(candidate, "FiveM.exe")))
                {
                    duplicatePath = candidate;
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
        }

        return false;
    }

    private bool HasWritePermission()
    {
        var dataRoot = Path.Combine(fiveMAppRoot, "data");
        var probeDirectory = Directory.Exists(dataRoot) ? dataRoot : fiveMAppRoot;
        if (!Directory.Exists(probeDirectory))
        {
            return true;
        }

        var probeFile = Path.Combine(probeDirectory, $".fivemcleaner-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probeFile, [0]);
            File.Delete(probeFile);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private bool IsUnderOneDrive()
    {
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var oneDrivePath = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(oneDrivePath))
            {
                continue;
            }

            try
            {
                var normalized = SafePath.Normalize(oneDrivePath);
                if (fiveMInstallationRoot.StartsWith(
                    normalized + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return false;
    }

    private bool TryGetLowFreeSpace(out double freeGiB)
    {
        freeGiB = 0;
        try
        {
            var root = Path.GetPathRoot(fiveMInstallationRoot);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            freeGiB = drive.AvailableFreeSpace / (double)GiB;
            return freeGiB < MinimumFreeSpaceGiB;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed class CrashPatternDiagnosisAction : WindowsOptimizationAction
{
    private readonly string fiveMAppRoot;

    public CrashPatternDiagnosisAction(string fiveMAppRoot)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseCrashPatterns);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parts = new List<string>();

        var crashesDirectory = Path.Combine(fiveMAppRoot, "crashes");
        if (Directory.Exists(crashesDirectory))
        {
            try
            {
                var fileNames = Directory.EnumerateFiles(crashesDirectory)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .ToArray();
                var recurring = FiveMLogPatterns.CountRecurringCrashCodes(fileNames);
                if (recurring.Count > 0)
                {
                    var codes = string.Join(", ", recurring.Select(pair => $"{pair.Key} ({pair.Value}x)"));
                    parts.Add($"Código(s) de erro recorrente(s) nos dumps recentes: {codes}.");
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                parts.Add($"Não foi possível listar os dumps recentes ({exception.Message}).");
            }
        }

        var logTail = FiveMLogPatterns.ReadLatestLogTail(fiveMAppRoot);
        if (logTail is not null)
        {
            var streamingKeywords = FiveMLogPatterns.FindStreamingErrorKeywords(logTail);
            if (streamingKeywords.Count > 0)
            {
                parts.Add($"Possíveis erros de streaming de conteúdo no log recente ({string.Join(", ", streamingKeywords)}).");
            }
        }

        var message = parts.Count == 0
            ? "Nenhum padrão recorrente de erro ou de streaming foi encontrado nos dados locais disponíveis."
            : string.Join(" ", parts) + " Isso não é uma análise de despejo de memória; use como indício, não como diagnóstico definitivo.";
        return Task.FromResult(WindowsActionApplyResult.NoChange(message));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal sealed record TerminatedProcessSnapshot(int ProcessId, string ProcessName);

public sealed class StuckProcessTerminationAction : WindowsOptimizationAction
{
    private readonly string fiveMInstallationRoot;
    private readonly IStuckFiveMProcessInspector inspector;

    public StuckProcessTerminationAction(
        string fiveMInstallationRoot,
        IStuckFiveMProcessInspector inspector)
    {
        this.fiveMInstallationRoot = SafePath.Normalize(fiveMInstallationRoot);
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.TerminateStuckFiveMProcess);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = inspector.GetSnapshot(fiveMInstallationRoot);
        if (!snapshot.Found)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum processo travado do FiveM foi encontrado; nada para encerrar."));
        }

        if (!inspector.TryTerminate(snapshot.ProcessId))
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Processo travado '{snapshot.ProcessName}' (PID {snapshot.ProcessId}) foi encontrado, mas não foi possível encerrá-lo agora."));
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new TerminatedProcessSnapshot(snapshot.ProcessId, snapshot.ProcessName),
            $"Processo travado '{snapshot.ProcessName}' (PID {snapshot.ProcessId}) foi encerrado."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        // Irreversible by nature: a terminated process cannot be restored.
        return Task.CompletedTask;
    }
}

public sealed class RecreateFiveMLocalDataAction : QuarantineCleanupAction
{
    private readonly string fiveMAppRoot;
    private readonly string installationRoot;
    private readonly IFiveMProcessInspector processInspector;

    public RecreateFiveMLocalDataAction(
        string fiveMAppRoot,
        string installationRoot,
        IFiveMProcessInspector processInspector,
        SafeFileTree? fileTree = null)
        : base(fileTree)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
        this.installationRoot = SafePath.Normalize(installationRoot);
        _ = SafePath.EnsureDescendant(this.installationRoot, this.fiveMAppRoot);
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.RecreateFiveMLocalData);

    protected override IReadOnlyList<CleanupScope> GetScopes(WindowsActionContext context)
    {
        if (processInspector.IsRunningFrom(installationRoot))
        {
            throw new InvalidOperationException("FiveM precisa estar fechado para recriar os dados locais.");
        }

        var dataRoot = SafePath.EnsureDescendant(fiveMAppRoot, Path.Combine(fiveMAppRoot, "data"));
        var matchAll = context.StartedAtUtc.AddMinutes(1);
        return
        [
            new CleanupScope(
                "server-cache",
                SafePath.EnsureDescendant(dataRoot, Path.Combine(dataRoot, "server-cache")),
                matchAll),
            new CleanupScope(
                "server-cache-priv",
                SafePath.EnsureDescendant(dataRoot, Path.Combine(dataRoot, "server-cache-priv")),
                matchAll),
            new CleanupScope(
                "logs",
                SafePath.EnsureDescendant(fiveMAppRoot, Path.Combine(fiveMAppRoot, "logs")),
                matchAll),
            new CleanupScope(
                "crashes",
                SafePath.EnsureDescendant(fiveMAppRoot, Path.Combine(fiveMAppRoot, "crashes")),
                matchAll)
        ];
    }
}

internal sealed record QuarantinedAuthItem(string OriginalPath, string QuarantinePath, bool IsDirectory);

internal sealed record AuthDataRepairSnapshot(IReadOnlyList<QuarantinedAuthItem> Items);

public sealed class StaleAuthDataRepairAction : WindowsOptimizationAction
{
    private readonly string fiveMAppRoot;
    private readonly string rosIdPath;
    private readonly string digitalEntitlementsRoot;
    private readonly string quarantineRoot;
    private readonly string installationRoot;
    private readonly IFiveMProcessInspector processInspector;

    public StaleAuthDataRepairAction(
        string fiveMAppRoot,
        string installationRoot,
        string rosIdPath,
        string digitalEntitlementsRoot,
        string quarantineRoot,
        IFiveMProcessInspector processInspector)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
        this.installationRoot = SafePath.Normalize(installationRoot);
        this.rosIdPath = SafePath.Normalize(rosIdPath);
        this.digitalEntitlementsRoot = SafePath.Normalize(digitalEntitlementsRoot);
        this.quarantineRoot = SafePath.Normalize(quarantineRoot);
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.RepairStaleAuthData);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (processInspector.IsRunningFrom(installationRoot))
        {
            throw new InvalidOperationException("FiveM precisa estar fechado para reparar os dados de entitlement.");
        }

        var logTail = FiveMLogPatterns.ReadLatestLogTail(fiveMAppRoot);
        if (logTail is null || !FiveMLogPatterns.ContainsEntitlementFailurePattern(logTail))
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum padrão conhecido de erro de entitlement foi encontrado no log recente; nada foi removido."));
        }

        var transactionQuarantine = SafePath.EnsureDescendant(
            quarantineRoot,
            Path.Combine(quarantineRoot, context.TransactionId.ToString("N")));
        var moved = new List<QuarantinedAuthItem>();

        try
        {
            if (File.Exists(rosIdPath))
            {
                SafePath.EnsureNoReparsePoints(rosIdPath);
                var destination = Path.Combine(transactionQuarantine, Path.GetFileName(rosIdPath));
                Directory.CreateDirectory(transactionQuarantine);
                File.Move(rosIdPath, destination, overwrite: false);
                moved.Add(new QuarantinedAuthItem(rosIdPath, destination, IsDirectory: false));
            }

            if (Directory.Exists(digitalEntitlementsRoot))
            {
                SafePath.EnsureNoReparsePoints(digitalEntitlementsRoot);
                var destination = Path.Combine(transactionQuarantine, Path.GetFileName(digitalEntitlementsRoot));
                Directory.CreateDirectory(transactionQuarantine);
                Directory.Move(digitalEntitlementsRoot, destination);
                moved.Add(new QuarantinedAuthItem(digitalEntitlementsRoot, destination, IsDirectory: true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreItems(moved);
            throw;
        }

        if (moved.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Padrão de erro de entitlement encontrado, mas nenhum dos arquivos esperados existe no momento."));
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new AuthDataRepairSnapshot(moved),
            $"{moved.Count} item(ns) de entitlement movido(s) para quarentena; será necessário novo login."));
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

        var snapshot = WindowsActionSnapshot.Deserialize<AuthDataRepairSnapshot>(snapshotJson);
        foreach (var item in snapshot.Items)
        {
            if (item.IsDirectory && Directory.Exists(item.QuarantinePath))
            {
                Directory.Delete(item.QuarantinePath, recursive: true);
            }
            else if (!item.IsDirectory && File.Exists(item.QuarantinePath))
            {
                File.Delete(item.QuarantinePath);
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

        var snapshot = WindowsActionSnapshot.Deserialize<AuthDataRepairSnapshot>(snapshotJson);
        RestoreItems(snapshot.Items);
        return Task.CompletedTask;
    }

    private static void RestoreItems(IReadOnlyList<QuarantinedAuthItem> items)
    {
        foreach (var item in items.Reverse())
        {
            if (item.IsDirectory)
            {
                if (Directory.Exists(item.QuarantinePath) && !Directory.Exists(item.OriginalPath))
                {
                    Directory.Move(item.QuarantinePath, item.OriginalPath);
                }
            }
            else if (File.Exists(item.QuarantinePath) && !File.Exists(item.OriginalPath))
            {
                File.Move(item.QuarantinePath, item.OriginalPath);
            }
        }
    }
}
