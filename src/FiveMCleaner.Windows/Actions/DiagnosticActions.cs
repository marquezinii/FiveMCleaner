using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Read-only actions that never change the machine. They always resolve to
/// <see cref="WindowsActionApplyResult.NoChange"/> with an informative
/// message and are never critical: a failure to read a signal degrades to a
/// generic message instead of aborting the run.
/// </summary>
public sealed class BottleneckDiagnosisAction : WindowsOptimizationAction
{
    private const long GiB = 1024L * 1024L * 1024L;
    private readonly ISystemResourceInspector inspector;

    public BottleneckDiagnosisAction(ISystemResourceInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseBottleneck);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var snapshot = inspector.GetSnapshot();
            return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(snapshot)));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Não foi possível ler os sinais de hardware para o diagnóstico de gargalo ({exception.Message})."));
        }
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    internal static string Classify(SystemResourceSnapshot snapshot)
    {
        var availableRatio = snapshot.TotalMemoryBytes > 0
            ? (double)snapshot.AvailableMemoryBytes / snapshot.TotalMemoryBytes
            : 1d;
        var freeDiskGiB = snapshot.SystemDriveFreeBytes / (double)GiB;

        if (availableRatio < 0.10 || snapshot.AvailableMemoryBytes < 1536L * 1024 * 1024)
        {
            return "Gargalo provável: memória RAM sob pressão. Feche outros programas antes de jogar.";
        }

        if (snapshot.LogicalProcessorCount <= 4)
        {
            return "Gargalo provável: poucos processadores lógicos, o que pode limitar servidores com muitos recursos/scripts.";
        }

        if (freeDiskGiB < 8)
        {
            return "Gargalo provável: pouco espaço livre em disco, o que pode atrasar carregamento de texturas e streaming de conteúdo.";
        }

        return "Nenhum gargalo evidente foi identificado; o hardware parece equilibrado para a carga atual.";
    }
}

public sealed class OverlaySoftwareDetectionAction : WindowsOptimizationAction
{
    private readonly IOverlaySoftwareInspector inspector;

    public OverlaySoftwareDetectionAction(IOverlaySoftwareInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DetectOverlaysAndCaptureSoftware);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var found = inspector.DetectRunningOverlayNames();
        var message = found.Count == 0
            ? "Nenhum overlay ou software de captura conhecido foi detectado em execução."
            : $"Overlay(s) detectado(s): {string.Join(", ", found)}. Nenhum deles foi fechado; feche manualmente se notar instabilidade.";
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

public sealed class FiveMLegacyLogReaderAction : WindowsOptimizationAction
{
    private const long MaxTailBytes = 512 * 1024;
    private readonly string fiveMAppRoot;

    public FiveMLegacyLogReaderAction(string fiveMAppRoot)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ReadFiveMLegacyLogs);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logsDirectory = Path.Combine(fiveMAppRoot, "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum log recente do FiveM foi encontrado; nada a analisar."));
        }

        FileInfo? latest = null;
        try
        {
            latest = new DirectoryInfo(logsDirectory)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Não foi possível listar os logs do FiveM ({exception.Message})."));
        }

        if (latest is null)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum log recente do FiveM foi encontrado; nada a analisar."));
        }

        var errorHits = 0;
        try
        {
            errorHits = CountPossibleErrors(latest.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var ageInaccessible = DateTimeOffset.UtcNow - latest.LastWriteTimeUtc;
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Log mais recente: {latest.Name}, modificado há {FormatAge(ageInaccessible)}. "
                    + $"Não foi possível ler o conteúdo agora ({exception.Message})."));
        }

        var age = DateTimeOffset.UtcNow - latest.LastWriteTimeUtc;
        var message = errorHits > 0
            ? $"Log mais recente: {latest.Name}, modificado há {FormatAge(age)}. "
                + $"{errorHits} linha(s) com possíveis erros; não é um diagnóstico definitivo."
            : $"Log mais recente: {latest.Name}, modificado há {FormatAge(age)}. "
                + "Nenhuma linha com possível erro foi encontrada.";
        return Task.FromResult(WindowsActionApplyResult.NoChange(message));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static int CountPossibleErrors(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxTailBytes)
        {
            stream.Seek(-MaxTailBytes, SeekOrigin.End);
        }

        using var reader = new StreamReader(stream);
        var count = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalDays >= 1
            ? $"{(int)age.TotalDays} dia(s)"
            : age.TotalHours >= 1
                ? $"{(int)age.TotalHours} hora(s)"
                : $"{Math.Max(1, (int)age.TotalMinutes)} minuto(s)";
    }
}

public sealed class PerformanceDiagnosticsGuideAction : WindowsOptimizationAction
{
    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.GuidePerformanceDiagnostics);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(
            "Use os comandos oficiais do FiveM no console (F8) para medir o desempenho real: "
                + "cl_drawfps true (FPS), cl_drawperf true (FPS/ping/CPU/GPU) e netgraph true (rede)."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
