using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;
using Microsoft.Win32;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Read-only diagnostic that never changes the machine. A failure to read a
/// signal degrades to a generic message instead of aborting the run.
/// </summary>
public sealed class BottleneckDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly ISystemResourceInspector inspector;

    public BottleneckDiagnosisAction(ISystemResourceInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseBottleneck);

    protected override string Describe()
    {
        try
        {
            return Classify(inspector.GetSnapshot());
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return "Não foi possível ler os sinais de hardware para o diagnóstico de gargalo "
                + $"({exception.Message}).";
        }
    }

    internal static string Classify(SystemResourceSnapshot snapshot)
    {
        if (DiagnosticSignals.IsMemoryUnderPressure(snapshot))
        {
            return "Gargalo provável: memória RAM sob pressão. Feche outros programas antes de jogar.";
        }

        if (snapshot.LogicalProcessorCount <= 4)
        {
            return "Gargalo provável: poucos processadores lógicos, o que pode limitar servidores com muitos recursos/scripts.";
        }

        if (snapshot.SystemDriveFreeBytes / (double)DiagnosticSignals.GiB < 8)
        {
            return "Gargalo provável: pouco espaço livre em disco, o que pode atrasar carregamento de texturas e streaming de conteúdo.";
        }

        return "Nenhum gargalo evidente foi identificado; o hardware parece equilibrado para a carga atual.";
    }
}

public sealed class OverlaySoftwareDetectionAction : ReadOnlyDiagnosticAction
{
    private readonly IOverlaySoftwareInspector inspector;

    public OverlaySoftwareDetectionAction(IOverlaySoftwareInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DetectOverlaysAndCaptureSoftware);

    protected override string Describe()
    {
        var found = inspector.DetectRunningOverlayNames();
        if (found.Count == 0)
        {
            return "Nenhum overlay ou software de captura conhecido foi detectado em execução.";
        }

        var message = $"Overlay(s) detectado(s): {string.Join(", ", found)}. Nenhum deles foi fechado; "
            + "feche manualmente se notar instabilidade.";
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

        return message;
    }
}

public sealed class FiveMLegacyLogReaderAction : ReadOnlyDiagnosticAction
{
    private const long MaxTailBytes = 512 * 1024;
    private const string NoLogsMessage = "Nenhum log recente do FiveM foi encontrado; nada a analisar.";

    private readonly string fiveMAppRoot;

    public FiveMLegacyLogReaderAction(string fiveMAppRoot)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ReadFiveMLegacyLogs);

    protected override string Describe()
    {
        var logsDirectory = Path.Combine(fiveMAppRoot, "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return NoLogsMessage;
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
            return $"Não foi possível listar os logs do FiveM ({exception.Message}).";
        }

        if (latest is null)
        {
            return NoLogsMessage;
        }

        var header = $"Log mais recente: {latest.Name}, modificado há "
            + $"{FormatAge(DateTimeOffset.UtcNow - latest.LastWriteTimeUtc)}.";
        try
        {
            var errorHits = CountPossibleErrors(latest.FullName);
            return errorHits > 0
                ? $"{header} {errorHits} linha(s) com possíveis erros; não é um diagnóstico definitivo."
                : $"{header} Nenhuma linha com possível erro foi encontrada.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"{header} Não foi possível ler o conteúdo agora ({exception.Message}).";
        }
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

public sealed class PerformanceDiagnosticsGuideAction : ReadOnlyDiagnosticAction
{
    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.GuidePerformanceDiagnostics);

    protected override string Describe()
    {
        return "Use os comandos oficiais do FiveM no console (F8) para medir o desempenho real: "
            + "cl_drawfps true (FPS), cl_drawperf true (FPS/ping/CPU/GPU), netgraph true (rede) e, "
            + "com o modo de desenvolvimento disponível, resmon true (CPU/memória por recurso do servidor). "
            + "O painel de prontidão para streaming do próprio FiveMCleaner mostra sinais adicionais de sessão.";
    }
}

public sealed class NetworkHealthDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly INetworkHealthInspector inspector;

    public NetworkHealthDiagnosisAction(INetworkHealthInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseNetworkHealth);

    protected override string Describe()
    {
        try
        {
            return Classify(inspector.GetSnapshot());
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return $"Não foi possível ler as estatísticas de rede ({exception.Message}).";
        }
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

public sealed class ThermalDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IThermalInspector inspector;

    public ThermalDiagnosisAction(IThermalInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseThermalThrottling);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(ThermalSnapshot snapshot)
    {
        if (!snapshot.IsAvailable || snapshot.HighestCelsius is not { } celsius)
        {
            return "Este computador não expõe uma leitura confiável de temperatura sem software do "
                + "fabricante da placa-mãe/GPU. Se notar quedas de desempenho sob carga prolongada, "
                + "verifique a temperatura com o utilitário oficial do fabricante.";
        }

        return DiagnosticSignals.IsTemperatureElevated(snapshot)
            ? $"Temperatura elevada detectada (~{celsius:0}°C); pode haver throttling térmico sob carga."
            : $"Temperatura dentro de uma faixa normal (~{celsius:0}°C) no momento da leitura.";
    }
}

public sealed class PagefileCommitDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const double LowAvailablePageFileRatio = 0.10d;

    private readonly ISystemResourceInspector inspector;

    public PagefileCommitDiagnosisAction(ISystemResourceInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnosePagefileCommit);

    protected override string Describe()
    {
        try
        {
            return Classify(inspector.GetSnapshot());
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return $"Não foi possível ler o estado do pagefile ({exception.Message}).";
        }
    }

    internal static string Classify(SystemResourceSnapshot snapshot)
    {
        if (snapshot.TotalPageFileBytes <= 0)
        {
            return "Não foi possível ler o tamanho do arquivo de paginação neste momento.";
        }

        var availableRatio = (double)snapshot.AvailablePageFileBytes / snapshot.TotalPageFileBytes;
        var totalGiB = snapshot.TotalPageFileBytes / (double)DiagnosticSignals.GiB;

        return availableRatio < LowAvailablePageFileRatio
            ? $"O commit de memória está próximo do limite do pagefile ({totalGiB:0.#} GB no total); "
                + "risco de lentidão por paginação excessiva sob carga."
            : $"Há folga suficiente no pagefile ({totalGiB:0.#} GB no total) para a carga atual.";
    }
}

public sealed class CacheIndexIntegrityDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly string fiveMAppRoot;

    public CacheIndexIntegrityDiagnosisAction(string fiveMAppRoot)
    {
        this.fiveMAppRoot = SafePath.Normalize(fiveMAppRoot);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseCacheIntegrity);

    protected override string Describe()
    {
        var dataRoot = Path.Combine(fiveMAppRoot, "data");
        var existing = new[]
        {
            Path.Combine(dataRoot, "server-cache", "content_index.xml"),
            Path.Combine(dataRoot, "server-cache-priv", "content_index.xml")
        }.Where(File.Exists).ToArray();

        if (existing.Length == 0)
        {
            return "Nenhum índice de cache foi encontrado (normal se o cache nunca foi usado ou já foi limpo).";
        }

        var corrupted = existing
            .Where(path => !IsWellFormedXml(path))
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path))
            .ToArray();

        return corrupted.Length > 0
            ? $"Índice de cache aparentemente corrompido: {string.Join(", ", corrupted)}. "
                + "Recomendamos usar o reparo de cache (perfil Médio/Agressivo com reparo habilitado) para reconstruí-lo."
            : "O índice de cache do FiveM está bem formado; nenhuma corrupção conhecida foi encontrada.";
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

public sealed class GpuVendorDetectionAction : ReadOnlyDiagnosticAction
{
    private static readonly (string Vendor, string Link)[] OfficialDriverLinks =
    [
        ("NVIDIA", "NVIDIA: nvidia.com/drivers"),
        ("AMD", "AMD: drivers.amd.com"),
        ("Intel", "Intel: intel.com/content/www/us/en/download-center/home.html")
    ];

    private readonly IGpuVendorInspector inspector;

    public GpuVendorDetectionAction(IGpuVendorInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DetectGpuVendor);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(GpuVendorSnapshot snapshot)
    {
        if (snapshot.DriverDescriptions.Count == 0)
        {
            return "Não foi possível identificar o fabricante da GPU neste momento.";
        }

        var vendors = snapshot.DriverDescriptions.Select(GpuVendorClassifier.VendorOf).ToArray();
        var described = snapshot.DriverDescriptions.Select(
            (description, index) => $"{vendors[index]} ({description})");

        var message = $"GPU(s) detectada(s): {string.Join(", ", described)}. Ajustes de perfil 3D devem ser feitos "
            + "apenas pelo painel oficial do fabricante (NVIDIA Control Panel, AMD Software ou Intel "
            + "Graphics Command Center); o FiveMCleaner não escreve nem sobrescreve esses perfis.";

        var links = OfficialDriverLinks
            .Where(entry => vendors.Contains(entry.Vendor, StringComparer.Ordinal))
            .Select(entry => entry.Link)
            .ToArray();
        return links.Length > 0
            ? message + $" Baixe o driver mais recente direto do fabricante: {string.Join("; ", links)}."
            : message;
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
public sealed class GpuPreferenceMismatchDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const string PreferencesSubKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

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

    protected override string Describe()
    {
        var descriptions = gpuVendor.GetSnapshot().DriverDescriptions;
        var hasIntegrated = descriptions.Any(GpuVendorClassifier.IsIntegrated);
        var hasDedicated = descriptions.Any(description => !GpuVendorClassifier.IsIntegrated(description));
        if (!hasIntegrated || !hasDedicated)
        {
            return "Não foi detectado um par de GPU integrada + dedicada; esta verificação só se aplica a "
                + "notebooks com duas GPUs.";
        }

        return IsHighPerformancePreferenceConfigured()
            ? "Duas GPUs detectadas e o FiveM já está configurado para preferir a GPU de alto desempenho."
            : "Duas GPUs detectadas (uma integrada e uma dedicada), mas o FiveM não está configurado para "
                + "preferir a GPU de alto desempenho nas preferências gráficas do Windows -- ative a opção "
                + "correspondente para evitar que o jogo rode na GPU integrada por engano.";
    }

    private bool IsHighPerformancePreferenceConfigured()
    {
        var value = registry.Read(new RegistryAddress(
            RegistryHive.CurrentUser,
            PreferencesSubKey,
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
public sealed class HybridLaptopDiagnosisAction : ReadOnlyDiagnosticAction
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

    protected override string Describe()
    {
        var onAc = powerStatus.IsOnAcPower();
        return Classify(
            onAc,
            !onAc && powerStatus.IsBatterySaverActive(),
            vendorSoftware.DetectInstalledToolNames());
    }

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

/// <summary>
/// Read-only guidance about very-high-polling-rate mice (4000/8000 Hz)
/// increasing CPU interrupt overhead, shown only when the CPU is currently
/// under heavy load. This app has no public, reliable way to read a mouse's
/// actual USB polling rate (it would require querying the raw USB
/// descriptor, not exposed by a documented Windows API) or to correlate
/// stutter with mouse movement in real time (this product only takes
/// point-in-time snapshots, not continuous telemetry) -- so this stays
/// text guidance tied to an existing, real signal (CPU load), never a
/// claim of having detected the mouse or its actual polling rate.
/// </summary>
public sealed class MousePollingRateGuidanceAction : ReadOnlyDiagnosticAction
{
    private const double HighCpuLoadPercent = 85d;

    private readonly IResourceUsageInspector resourceUsage;

    public MousePollingRateGuidanceAction(IResourceUsageInspector resourceUsage)
    {
        this.resourceUsage = resourceUsage ?? throw new ArgumentNullException(nameof(resourceUsage));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.GuideMousePollingRate);

    protected override string Describe() => Classify(resourceUsage.GetSnapshot().CpuPercent);

    internal static string Classify(double? cpuPercent)
    {
        if (cpuPercent is { } percent && percent >= HighCpuLoadPercent)
        {
            return $"CPU sob carga alta agora ({percent:0}%). Se você usa um mouse configurado para 4000 Hz "
                + "ou 8000 Hz de polling e nota stutter que parece coincidir com o movimento do mouse, teste "
                + "reduzir para 1000 Hz -- taxas muito altas aumentam a sobrecarga de interrupções da CPU, "
                + "que pode ser perceptível justamente quando a CPU já está no limite.";
        }

        return "CPU não está sob carga alta neste momento. Se notar stutter que parece coincidir com o "
            + "movimento do mouse em algum jogo, e ele estiver configurado para 4000 Hz ou 8000 Hz de "
            + "polling, teste reduzir para 1000 Hz como diagnóstico -- este app não consegue ler a taxa de "
            + "polling real do seu mouse nem correlacionar isso com stutter automaticamente.";
    }
}
