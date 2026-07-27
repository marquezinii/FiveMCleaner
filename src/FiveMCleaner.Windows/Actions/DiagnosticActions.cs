using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;
using Microsoft.Win32;

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
        if (found.Any(name => name.Contains("ShadowPlay", StringComparison.OrdinalIgnoreCase)))
        {
            // "NVIDIA Share" is the actual process behind Instant Replay; its
            // presence is the closest reliable, read-only signal this
            // product has for it. Freestyle filters run inside the same
            // overlay and have no separate process signal, so this can only
            // suggest checking manually, never assert filters are active.
            message += " O processo do NVIDIA Share/ShadowPlay pode indicar Instant Replay ativo; "
                + "se filtros do Freestyle estiverem configurados, também podem estar em uso -- confira no NVIDIA App.";
        }

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
                + "cl_drawfps true (FPS), cl_drawperf true (FPS/ping/CPU/GPU), netgraph true (rede) e, "
                + "com o modo de desenvolvimento disponível, resmon true (CPU/memória por recurso do servidor). "
                + "O painel de prontidão para streaming do próprio FiveMCleaner mostra sinais adicionais de sessão."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class NetworkHealthDiagnosisAction : WindowsOptimizationAction
{
    private readonly INetworkHealthInspector inspector;

    public NetworkHealthDiagnosisAction(INetworkHealthInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseNetworkHealth);

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
                $"Não foi possível ler as estatísticas de rede ({exception.Message})."));
        }
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    internal static string Classify(NetworkHealthSnapshot snapshot)
    {
        if (!snapshot.HasActiveInterface)
        {
            return "Não foi possível ler estatísticas de nenhuma placa de rede ativa no momento.";
        }

        if (snapshot.DiscardedPackets > 0 || snapshot.ErrorPackets > 0)
        {
            return $"Sinais locais de instabilidade de rede: {snapshot.DiscardedPackets} pacote(s) descartado(s) "
                + $"e {snapshot.ErrorPackets} com erro na(s) placa(s) ativa(s). Isso não mede jitter até o "
                + "servidor do FiveM; use netgraph dentro do jogo para isso.";
        }

        return "Nenhum sinal local de perda de pacotes foi encontrado nas placas de rede ativas.";
    }
}

public sealed class ThermalDiagnosisAction : WindowsOptimizationAction
{
    private const double ElevatedTemperatureCelsius = 85d;
    private readonly IThermalInspector inspector;

    public ThermalDiagnosisAction(IThermalInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseThermalThrottling);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = inspector.GetSnapshot();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(snapshot)));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    internal static string Classify(ThermalSnapshot snapshot)
    {
        if (!snapshot.IsAvailable || snapshot.HighestCelsius is not { } celsius)
        {
            return "Este computador não expõe uma leitura confiável de temperatura sem software do "
                + "fabricante da placa-mãe/GPU. Se notar quedas de desempenho sob carga prolongada, "
                + "verifique a temperatura com o utilitário oficial do fabricante.";
        }

        return celsius >= ElevatedTemperatureCelsius
            ? $"Temperatura elevada detectada (~{celsius:0}°C); pode haver throttling térmico sob carga."
            : $"Temperatura dentro de uma faixa normal (~{celsius:0}°C) no momento da leitura.";
    }
}

public sealed class PagefileCommitDiagnosisAction : WindowsOptimizationAction
{
    private const long GiB = 1024L * 1024L * 1024L;
    private readonly ISystemResourceInspector inspector;

    public PagefileCommitDiagnosisAction(ISystemResourceInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnosePagefileCommit);

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
                $"Não foi possível ler o estado do pagefile ({exception.Message})."));
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
        if (snapshot.TotalPageFileBytes <= 0)
        {
            return "Não foi possível ler o tamanho do arquivo de paginação neste momento.";
        }

        var availableRatio = (double)snapshot.AvailablePageFileBytes / snapshot.TotalPageFileBytes;
        var totalGiB = snapshot.TotalPageFileBytes / (double)GiB;

        return availableRatio < 0.10
            ? $"O commit de memória está próximo do limite do pagefile ({totalGiB:0.#} GB no total); "
                + "risco de lentidão por paginação excessiva sob carga."
            : $"Há folga suficiente no pagefile ({totalGiB:0.#} GB no total) para a carga atual.";
    }
}

public sealed class CacheIndexIntegrityDiagnosisAction : WindowsOptimizationAction
{
    private readonly string fiveMAppRoot;

    public CacheIndexIntegrityDiagnosisAction(string fiveMAppRoot)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseCacheIntegrity);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataRoot = Path.Combine(fiveMAppRoot, "data");
        var candidates = new[]
        {
            Path.Combine(dataRoot, "server-cache", "content_index.xml"),
            Path.Combine(dataRoot, "server-cache-priv", "content_index.xml")
        };

        var corrupted = new List<string>();
        var checkedAny = false;
        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            checkedAny = true;
            if (!IsWellFormedXml(path))
            {
                corrupted.Add(Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path));
            }
        }

        if (!checkedAny)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum índice de cache foi encontrado (normal se o cache nunca foi usado ou já foi limpo)."));
        }

        if (corrupted.Count > 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Índice de cache aparentemente corrompido: {string.Join(", ", corrupted)}. "
                    + "Recomendamos usar o reparo de cache (perfil Médio/Agressivo com reparo habilitado) para reconstruí-lo."));
        }

        return Task.FromResult(WindowsActionApplyResult.NoChange(
            "O índice de cache do FiveM está bem formado; nenhuma corrupção conhecida foi encontrada."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static bool IsWellFormedXml(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = System.Xml.XmlReader.Create(stream);
            while (reader.Read())
            {
            }

            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked or inaccessible file is not evidence of corruption.
            return true;
        }
    }
}

public sealed class GpuVendorDetectionAction : WindowsOptimizationAction
{
    private readonly IGpuVendorInspector inspector;

    public GpuVendorDetectionAction(IGpuVendorInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DetectGpuVendor);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = inspector.GetSnapshot();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(snapshot)));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    internal static string Classify(GpuVendorSnapshot snapshot)
    {
        if (snapshot.DriverDescriptions.Count == 0)
        {
            return "Não foi possível identificar o fabricante da GPU neste momento.";
        }

        var vendorNames = snapshot.DriverDescriptions
            .Select(VendorOf)
            .ToArray();
        var vendors = snapshot.DriverDescriptions
            .Select((description, index) => $"{vendorNames[index]} ({description})")
            .ToArray();

        var message = $"GPU(s) detectada(s): {string.Join(", ", vendors)}. Ajustes de perfil 3D devem ser feitos "
            + "apenas pelo painel oficial do fabricante (NVIDIA Control Panel, AMD Software ou Intel "
            + "Graphics Command Center); o FiveMCleaner não escreve nem sobrescreve esses perfis.";

        var links = new List<string>();
        if (vendorNames.Contains("NVIDIA"))
        {
            links.Add("NVIDIA: nvidia.com/drivers");
        }

        if (vendorNames.Contains("AMD"))
        {
            links.Add("AMD: drivers.amd.com");
        }

        if (vendorNames.Contains("Intel"))
        {
            links.Add("Intel: intel.com/content/www/us/en/download-center/home.html");
        }

        if (links.Count > 0)
        {
            message += $" Baixe o driver mais recente direto do fabricante: {string.Join("; ", links)}.";
        }

        return message;
    }

    private static string VendorOf(string driverDescription)
    {
        if (driverDescription.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return "NVIDIA";
        }

        if (driverDescription.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || driverDescription.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return "AMD";
        }

        if (driverDescription.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "Intel";
        }

        return "Desconhecido";
    }
}

/// <summary>
/// Read-only cross-check between "this looks like a dual-GPU laptop" (from
/// <see cref="IGpuVendorInspector"/>'s driver descriptions) and "the
/// per-app GPU preference registry entry for FiveM is actually set to high
/// performance" (the same
/// <c>HKCU\Software\Microsoft\DirectX\UserGpuPreferences</c> location
/// <see cref="GpuPreferenceRegistryAction"/> writes to). Item from the
/// graphics optimizations backlog: "detectar quando o jogo está usando a
/// integrada por engano" -- deliberately scoped to what can be checked
/// without hooking the running game's actual DXGI adapter (which this
/// product does not do), never alters anything.
/// </summary>
public sealed class GpuPreferenceMismatchDiagnosisAction : WindowsOptimizationAction
{
    private static readonly string[] IntegratedMarkers =
        ["Intel(R) UHD", "Intel(R) HD", "Intel(R) Iris", "Intel UHD", "Intel HD", "Intel Iris", "AMD Radeon(TM) Graphics", "AMD Radeon Graphics"];

    private readonly IGpuVendorInspector gpuVendor;
    private readonly IRegistryStore registry;
    private readonly string fiveMExecutable;

    public GpuPreferenceMismatchDiagnosisAction(
        IGpuVendorInspector gpuVendor,
        IRegistryStore registry,
        string fiveMExecutablePath)
    {
        this.gpuVendor = gpuVendor ?? throw new ArgumentNullException(nameof(gpuVendor));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        fiveMExecutable = Path.GetFullPath(fiveMExecutablePath);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseGpuPreferenceMismatch);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = gpuVendor.GetSnapshot();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(snapshot)));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private string Classify(GpuVendorSnapshot snapshot)
    {
        var hasIntegrated = snapshot.DriverDescriptions.Any(IsIntegrated);
        var hasDedicated = snapshot.DriverDescriptions.Any(description => !IsIntegrated(description));
        if (!hasIntegrated || !hasDedicated)
        {
            return "Não foi detectado um par de GPU integrada + dedicada; esta verificação só se aplica a "
                + "notebooks com duas GPUs.";
        }

        var configured = IsHighPerformancePreferenceConfigured();
        return configured
            ? "Duas GPUs detectadas e o FiveM já está configurado para preferir a GPU de alto desempenho."
            : "Duas GPUs detectadas (uma integrada e uma dedicada), mas o FiveM não está configurado para "
                + "preferir a GPU de alto desempenho nas preferências gráficas do Windows -- ative a opção "
                + "correspondente para evitar que o jogo rode na GPU integrada por engano.";
    }

    private bool IsHighPerformancePreferenceConfigured()
    {
        var value = registry.Read(new RegistryAddress(
            RegistryHive.CurrentUser,
            @"Software\Microsoft\DirectX\UserGpuPreferences",
            fiveMExecutable));
        if (!value.Exists || value.Kind != RegistryValueKind.String || string.IsNullOrWhiteSpace(value.StringValue))
        {
            return false;
        }

        return value.StringValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Any(segment => segment.Equals("GpuPreference=2", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIntegrated(string driverDescription)
    {
        return IntegratedMarkers.Any(marker =>
            driverDescription.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Read-only guidance for hybrid/gaming laptops: reports whether the
/// machine is running on battery (dedicated-GPU/performance modes usually
/// only engage on AC) or with Windows Battery Saver active, and whether a
/// known manufacturer utility that exposes GPU-switch (MUX) or performance
/// mode controls (Armoury Crate, MSI Center, Lenovo Vantage, etc.) is
/// installed. Never controls a MUX switch or BIOS setting itself through an
/// undocumented, vendor-specific mechanism -- see
/// docs/graphics-optimizations-backlog.md, seção 12, for why that stays
/// out of scope. Thermal/power throttling itself is already covered by the
/// separate <c>safety.throttling-signal.diagnose</c> diagnostic; this
/// action does not duplicate that.
/// </summary>
public sealed class HybridLaptopDiagnosisAction : WindowsOptimizationAction
{
    private readonly IPowerStatusProvider powerStatus;
    private readonly IVendorLaptopSoftwareInspector vendorSoftware;

    public HybridLaptopDiagnosisAction(
        IPowerStatusProvider powerStatus,
        IVendorLaptopSoftwareInspector vendorSoftware)
    {
        this.powerStatus = powerStatus ?? throw new ArgumentNullException(nameof(powerStatus));
        this.vendorSoftware = vendorSoftware ?? throw new ArgumentNullException(nameof(vendorSoftware));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseHybridLaptop);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var onAc = powerStatus.IsOnAcPower();
        var batterySaver = !onAc && powerStatus.IsBatterySaverActive();
        var tools = vendorSoftware.DetectInstalledToolNames();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(onAc, batterySaver, tools)));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(bool onAc, bool batterySaverActive, IReadOnlyList<string> detectedTools)
    {
        var parts = new List<string>();
        if (!onAc)
        {
            parts.Add("O notebook está na bateria; modos de GPU dedicada e desempenho máximo do fabricante "
                + "costumam só se aplicar com o carregador conectado -- conecte-o antes de jogar para "
                + "melhor desempenho.");
        }

        if (batterySaverActive)
        {
            parts.Add("A Economia de Energia do Windows está ativa, o que reduz desempenho geral -- "
                + "desative-a antes de jogar.");
        }

        parts.Add(detectedTools.Count == 0
            ? "Nenhum utilitário conhecido de troca de GPU/modo de desempenho do fabricante do notebook "
                + "(Armoury Crate, MSI Center, Lenovo Vantage, etc.) foi detectado; se este notebook tiver "
                + "GPU dedicada e MUX switch, consulte o utilitário do fabricante para ativá-lo."
            : $"Utilitário(s) do fabricante detectado(s): {string.Join(", ", detectedTools)}. Use-o para "
                + "ativar o modo de GPU dedicada/MUX switch e o perfil de desempenho, se disponíveis -- "
                + "o FiveMCleaner não controla isso diretamente.");

        return string.Join(" ", parts);
    }
}
