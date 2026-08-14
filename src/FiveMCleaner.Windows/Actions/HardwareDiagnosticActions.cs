using System.Globalization;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;
using Microsoft.Win32;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Read-only hardware/system diagnostics. Every action here always resolves to
/// <see cref="WindowsActionApplyResult.NoChange"/> with an honest message; none
/// of them ever writes to the system, installs a driver, or claims data it
/// could not actually read.
/// </summary>
public sealed class CpuDetailsDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const double SignificantClockDropRatio = 0.5d;

    private readonly ICpuInspector inspector;

    public CpuDetailsDiagnosisAction(ICpuInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseCpuDetails);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(CpuSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "Não foi possível ler os detalhes da CPU neste momento.";
        }

        var message = $"{snapshot.PhysicalCores} núcleo(s) físico(s), {snapshot.LogicalThreads} thread(s) lógica(s). "
            + $"Frequência atual: {snapshot.CurrentClockMhz} MHz de {snapshot.MaxClockMhz} MHz máximos.";
        if (snapshot.MaxClockMhz > 0 && snapshot.CurrentClockMhz < snapshot.MaxClockMhz * SignificantClockDropRatio)
        {
            message += " A frequência atual está bem abaixo do máximo (economia de energia ou possível throttling).";
        }

        return message;
    }
}

public sealed class GpuDetailsDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IGpuDetailsInspector inspector;

    public GpuDetailsDiagnosisAction(IGpuDetailsInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseGpuDetails);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(IReadOnlyList<GpuAdapterDetails> adapters)
    {
        if (adapters.Count == 0)
        {
            return "Não foi possível detectar detalhes de VRAM/tipo da GPU neste momento.";
        }

        var parts = adapters.Select(adapter =>
        {
            var vram = adapter.VramBytes is > 0
                ? $"{adapter.VramBytes.Value / (double)DiagnosticSignals.GiB:0.#} GB de VRAM"
                : "VRAM não detectada";
            var kind = adapter.KindGuess switch
            {
                GpuKindGuess.LikelyIntegrated => "provavelmente integrada",
                GpuKindGuess.LikelyDiscrete => "provavelmente dedicada",
                _ => "tipo não identificado"
            };
            return $"{adapter.DriverDescription} ({vram}, {kind})";
        });

        return $"GPU(s) detectada(s): {string.Join("; ", parts)}.";
    }
}

public sealed class RamDetailsDiagnosisAction : ReadOnlyDiagnosticAction
{
    /// <summary>
    /// Tolerance below the module's rated (SPD) speed before XMP/EXPO is
    /// reported as probably disabled: reported clocks are rounded and vendors
    /// publish slightly different rated values, so an exact comparison would
    /// produce false alarms.
    /// </summary>
    private const double RatedClockTolerance = 0.9d;

    private readonly IRamDetailsInspector inspector;

    public RamDetailsDiagnosisAction(IRamDetailsInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseRamDetails);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(RamDetailsSnapshot snapshot)
    {
        if (snapshot.Modules.Count == 0)
        {
            return "Não foi possível ler os detalhes dos módulos de memória neste momento.";
        }

        var count = snapshot.Modules.Count;
        var configured = snapshot.Modules
            .Select(module => module.ConfiguredClockMhz)
            .Where(clock => clock > 0)
            .DefaultIfEmpty(0u)
            .Max();

        var channelHint = count == 1
            ? "provavelmente single-channel (apenas um pente instalado)"
            : count % 2 == 0
                ? "provavelmente multi-channel (quantidade par de pentes)"
                : "quantidade ímpar de pentes; a configuração de canais não pôde ser confirmada";

        var frequencyLabel = configured > 0
            ? $"{configured} MHz configurados"
            : "frequência configurada não disponível";

        return $"{count} módulo(s) de memória detectado(s), {frequencyLabel}. {channelHint}. "
            + BuildXmpHint(snapshot.Modules);
    }

    private static string BuildXmpHint(IReadOnlyList<RamModuleInfo> modules)
    {
        var withRated = modules
            .Where(module => module.RatedClockMhz > 0 && module.ConfiguredClockMhz > 0)
            .ToArray();
        if (withRated.Length == 0)
        {
            return "Não foi possível comparar a velocidade configurada com a velocidade nominal (XMP/EXPO).";
        }

        return withRated.Any(module => module.ConfiguredClockMhz < module.RatedClockMhz * RatedClockTolerance)
            ? "A memória parece rodar abaixo da velocidade nominal (XMP/EXPO possivelmente desativado)."
            : "A memória parece rodar na velocidade nominal ou acima (XMP/EXPO provavelmente ativo).";
    }
}

public sealed class StorageHealthDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IStorageHealthInspector inspector;

    public StorageHealthDiagnosisAction(IStorageHealthInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseStorageHealth);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(StorageHealthSnapshot snapshot)
    {
        if (snapshot.Disks.Count == 0)
        {
            return "Não foi possível ler o tipo/saúde das unidades físicas neste momento.";
        }

        var unhealthyCount = snapshot.Disks.Count(disk => !disk.IsHealthy);
        var summary = string.Join("; ", snapshot.Disks.Select(disk =>
            $"{disk.FriendlyName} ({disk.MediaTypeLabel}, {disk.HealthStatusLabel})"));

        return unhealthyCount > 0
            ? $"Atenção: {unhealthyCount} unidade(s) com alerta de saúde. Unidades detectadas: {summary}."
            : $"Todas as unidades detectadas relatam saúde normal. Unidades: {summary}.";
    }
}

public sealed class DriverVersionsDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const int OldVideoDriverThresholdMonths = 18;

    private readonly IDriverVersionInspector inspector;

    public DriverVersionsDiagnosisAction(IDriverVersionInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseDriverVersions);

    protected override string Describe()
    {
        var buildLabel = OperatingSystem.IsWindows()
            ? $"build {Environment.OSVersion.Version.Build}"
            : "build desconhecido";
        var snapshot = inspector.GetSnapshot();
        var message = $"Windows {buildLabel}. {Classify(snapshot)}";
        var oldDriverWarning = ClassifyOldDrivers(snapshot, DateTimeOffset.UtcNow);
        return oldDriverWarning is null ? message : $"{message} {oldDriverWarning}";
    }

    internal static string Classify(DriverVersionSnapshot snapshot)
    {
        var groups = new (string Label, IReadOnlyList<DriverVersionInfo> Items)[]
        {
            ("Vídeo", snapshot.Video),
            ("Rede", snapshot.Network),
            ("Áudio", snapshot.Audio),
            ("Chipset", snapshot.Chipset)
        };

        var parts = groups
            .Where(group => group.Items.Count > 0)
            .Select(group => $"{group.Label}: {string.Join(", ", group.Items.Select(item => $"{item.DeviceName} {item.DriverVersion}"))}");

        var joined = string.Join(" | ", parts);
        return string.IsNullOrEmpty(joined)
            ? "Não foi possível ler versões de driver de vídeo/rede/áudio/chipset."
            : joined;
    }

    /// <summary>
    /// Flags a video driver whose <c>DriverDate</c> (WMI-reported, the same
    /// date shown in Device Manager's Driver tab) is older than
    /// <see cref="OldVideoDriverThresholdMonths"/> months -- an objective,
    /// verifiable signal rather than guessing from the version string, which
    /// vendors format inconsistently. Only video drivers are checked: the
    /// graphics driver is what this product's optimizations actually depend on.
    /// Returns null when there is nothing to flag (date unavailable, or driver
    /// recent enough) -- never a false alarm.
    /// </summary>
    internal static string? ClassifyOldDrivers(DriverVersionSnapshot snapshot, DateTimeOffset now)
    {
        var threshold = now.AddMonths(-OldVideoDriverThresholdMonths);
        var old = snapshot.Video
            .Where(item => item.DriverDate is { } date && date < threshold)
            .ToArray();
        if (old.Length == 0)
        {
            return null;
        }

        var names = string.Join(", ", old.Select(item =>
            $"{item.DeviceName} ({item.DriverDate!.Value:yyyy-MM})"));
        return $"Driver de vídeo com mais de {OldVideoDriverThresholdMonths} meses sem atualização: {names}. "
            + "Considere atualizar pelo site oficial do fabricante.";
    }
}

/// <summary>
/// Read-only guidance for G-SYNC/VRR: orients the user on how to check and
/// enable it (NVIDIA Control Panel/monitor OSD) and on the FPS limit NVIDIA
/// itself recommends (a few frames below the monitor's maximum refresh
/// rate, to keep the frame rate inside G-SYNC's variable range) -- reusing
/// the same <c>-frameLimit</c> launch parameter this product already
/// supports. Never enables G-SYNC itself: there is no public, documented
/// API for that (see docs/graphics-optimizations-backlog.md, seção 10).
/// </summary>
public sealed class GSyncGuidanceDiagnosisAction : ReadOnlyDiagnosticAction
{
    /// <summary>
    /// Frames subtracted from the monitor's maximum refresh rate to keep the
    /// frame rate inside the variable range, as the vendors recommend.
    /// </summary>
    private const int VariableRangeHeadroomFps = 3;

    private readonly IDisplayConfigurationInspector inspector;
    private readonly IGpuVendorInspector gpuVendor;

    public GSyncGuidanceDiagnosisAction(
        IDisplayConfigurationInspector inspector,
        IGpuVendorInspector gpuVendor)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.gpuVendor = gpuVendor ?? throw new ArgumentNullException(nameof(gpuVendor));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.GuideGSync);

    protected override string Describe() => Classify(inspector.GetSnapshot(), gpuVendor.GetSnapshot());

    internal static string Classify(DisplayConfigurationSnapshot? snapshot, GpuVendorSnapshot gpuSnapshot)
    {
        var baseGuidance = "G-SYNC/FreeSync/VRR não tem uma API pública para ser ativado por este app; "
            + $"confirme e ative pelo {DescribeControlPanel(gpuSnapshot)} ou pelo menu do próprio monitor.";
        if (snapshot is null || snapshot.MaxRefreshHzAtCurrentResolution <= 0)
        {
            return baseGuidance;
        }

        var recommendedCap = Math.Max(1, snapshot.MaxRefreshHzAtCurrentResolution - VariableRangeHeadroomFps);
        return $"{baseGuidance} Para manter o FPS dentro da faixa variável desta tecnologia neste monitor "
            + $"({snapshot.MaxRefreshHzAtCurrentResolution} Hz no modo atual), o fabricante recomenda limitar "
            + $"o FPS a alguns quadros abaixo do máximo (por exemplo, {recommendedCap} FPS) -- use o "
            + "parâmetro -frameLimit do GTA V standalone para isso.";
    }

    /// <summary>
    /// Names the correct vendor panel (NVIDIA Control Panel for G-SYNC, AMD
    /// Software: Adrenalin Edition for FreeSync) when exactly one vendor is
    /// detected; otherwise falls back to a vendor-neutral phrasing rather
    /// than guessing which one applies.
    /// </summary>
    private static string DescribeControlPanel(GpuVendorSnapshot gpuSnapshot)
    {
        var vendors = gpuSnapshot.DriverDescriptions
            .Select(GpuVendorClassifier.VendorOf)
            .Distinct()
            .ToArray();

        return vendors switch
        {
            ["NVIDIA"] => "NVIDIA Control Panel (Configurar G-SYNC)",
            ["AMD"] => "AMD Software: Adrenalin Edition (FreeSync)",
            _ => "painel de controle oficial da sua GPU"
        };
    }
}

/// <summary>
/// Guided repair, never automatic: points the user to the official steps
/// for a clean NVIDIA driver reinstall (DDU in Safe Mode, then the latest
/// driver from nvidia.com/drivers) when there is a suspected driver
/// corruption issue. This product never downloads, installs, or removes a
/// display driver itself -- picking the wrong driver package or interrupting
/// a driver install can leave a machine without video output, which is
/// exactly the kind of irreversible, high-blast-radius risk this product's
/// safety model (docs/safety.md) exists to avoid.
/// </summary>
public sealed class GuidedDriverReinstallAction : ReadOnlyDiagnosticAction
{
    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.GuideDriverReinstall);

    protected override string Describe()
    {
        return "Reinstalação limpa guiada (nenhum arquivo foi tocado): 1) baixe o Display Driver Uninstaller "
            + "(DDU) e o instalador de driver mais recente do site oficial do fabricante da GPU antes de "
            + "começar; 2) reinicie o Windows em Modo de Segurança; 3) rode o DDU e remova apenas o "
            + "driver de vídeo; 4) reinicie normalmente e instale o driver baixado no passo 1. Válido "
            + "tanto para GPUs NVIDIA quanto AMD (Adrenalin). Este aplicativo não baixa, instala nem "
            + "remove nenhum driver automaticamente.";
    }
}

public sealed class DisplayConfigurationDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IDisplayConfigurationInspector inspector;

    public DisplayConfigurationDiagnosisAction(IDisplayConfigurationInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseDisplayConfiguration);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(DisplayConfigurationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "Não foi possível ler a configuração do monitor neste momento.";
        }

        var hags = snapshot.HardwareGpuScheduling switch
        {
            HardwareGpuSchedulingState.Enabled => "ativado",
            HardwareGpuSchedulingState.Disabled => "desativado",
            _ => "não suportado ou não informado pelo driver"
        };

        var refreshNote = snapshot.CurrentRefreshHz < snapshot.MaxRefreshHzAtCurrentResolution
            ? $" A taxa configurada ({snapshot.CurrentRefreshHz} Hz) está abaixo da máxima suportada nessa resolução ({snapshot.MaxRefreshHzAtCurrentResolution} Hz)."
            : string.Empty;

        return $"Monitor em {snapshot.Width}x{snapshot.Height} a {snapshot.CurrentRefreshHz} Hz.{refreshNote} "
            + $"Agendamento de GPU acelerado por hardware (HAGS): {hags}. G-SYNC/FreeSync/VRR não podem ser "
            + "detectados de forma confiável sem software do fabricante.";
    }
}

/// <summary>
/// Reads the session-relevant Windows settings. Unlike the other diagnostics
/// here it queries the active power plan asynchronously, so it implements
/// <see cref="WindowsOptimizationAction"/> directly instead of deriving from
/// <see cref="ReadOnlyDiagnosticAction"/>; it is still strictly read-only.
/// </summary>
public sealed class SessionSettingsDiagnosisAction : WindowsOptimizationAction
{
    private static readonly RegistryAddress GameModeAddress = new(
        RegistryHive.CurrentUser,
        @"Software\Microsoft\GameBar",
        "AutoGameModeEnabled");

    private static readonly RegistryAddress FullscreenOptimizationsAddress = new(
        RegistryHive.CurrentUser,
        @"System\GameConfigStore",
        "GameDVR_FSEBehaviorMode");

    private static readonly IReadOnlyDictionary<Guid, string> KnownPowerSchemes =
        new Dictionary<Guid, string>
        {
            [new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")] = "Balanceado",
            [new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")] = "Alto desempenho",
            [new Guid("a1841308-3541-4fab-bc81-f71556f20b4a")] = "Economia de energia",
            [new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")] = "Desempenho máximo"
        };

    private readonly IRegistryStore registry;
    private readonly IPowerPlanController powerPlans;

    public SessionSettingsDiagnosisAction(IRegistryStore registry, IPowerPlanController powerPlans)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.powerPlans = powerPlans ?? throw new ArgumentNullException(nameof(powerPlans));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseSessionSettings);

    public override async Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gameMode = registry.Read(GameModeAddress);
        var fullscreenOptimizations = registry.Read(FullscreenOptimizationsAddress);

        string powerPlanLabel;
        try
        {
            var scheme = await powerPlans.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
            powerPlanLabel = KnownPowerSchemes.TryGetValue(scheme, out var known) ? known : scheme.ToString("D");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            powerPlanLabel = "não foi possível ler";
        }

        return WindowsActionApplyResult.NoChange(Classify(gameMode, fullscreenOptimizations, powerPlanLabel));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(
        RegistryValueState gameMode,
        RegistryValueState fullscreenOptimizations,
        string powerPlanLabel)
    {
        var gameModeLabel = gameMode is { Exists: true, NumericValue: 1 } ? "ativado" : "desativado ou padrão do Windows";
        // GameDVR_FSEBehaviorMode = 2 means Windows-wide "Disable fullscreen optimizations" is on.
        var fseLabel = fullscreenOptimizations is { Exists: true, NumericValue: 2 }
            ? "desativadas (jogo em tela cheia exclusiva)"
            : "ativadas (padrão do Windows)";

        return $"Modo de Jogo: {gameModeLabel}. Otimizações para tela cheia: {fseLabel}. "
            + $"Plano de energia ativo: {powerPlanLabel}.";
    }
}

public sealed class ThrottlingSignalDiagnosisAction : ReadOnlyDiagnosticAction
{
    /// <summary>
    /// Frequency below this fraction of the maximum, while the CPU is busy,
    /// is what this product calls a clock drop under load.
    /// </summary>
    private const double ClockDropRatio = 0.6d;
    private const double BusyCpuPercent = 50d;

    private readonly ICpuInspector cpu;
    private readonly IResourceUsageInspector usage;
    private readonly IHardwareStabilityInspector stability;
    private readonly IThermalInspector thermal;

    public ThrottlingSignalDiagnosisAction(
        ICpuInspector cpu,
        IResourceUsageInspector usage,
        IHardwareStabilityInspector stability,
        IThermalInspector thermal)
    {
        this.cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
        this.usage = usage ?? throw new ArgumentNullException(nameof(usage));
        this.stability = stability ?? throw new ArgumentNullException(nameof(stability));
        this.thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseThrottlingSignal);

    protected override string Describe()
    {
        return Classify(
            cpu.GetSnapshot(),
            usage.GetSnapshot(),
            stability.GetSnapshot(),
            thermal.GetSnapshot());
    }

    internal static string Classify(
        CpuSnapshot? cpuSnapshot,
        ResourceUsageSnapshot usageSnapshot,
        HardwareStabilitySnapshot stabilitySnapshot,
        ThermalSnapshot thermalSnapshot)
    {
        var clockDropUnderLoad = cpuSnapshot is not null
            && cpuSnapshot.MaxClockMhz > 0
            && cpuSnapshot.CurrentClockMhz < cpuSnapshot.MaxClockMhz * ClockDropRatio
            && usageSnapshot.CpuPercent > BusyCpuPercent;
        var wheaSignal = stabilitySnapshot.RecentWheaEventCount > 0;
        var thermalSignal = DiagnosticSignals.IsTemperatureElevated(thermalSnapshot);

        if (clockDropUnderLoad && (thermalSignal || wheaSignal))
        {
            return "Possível throttling detectado: queda de frequência sob carga combinada com "
                + (thermalSignal ? "temperatura elevada" : "eventos de erro de hardware (WHEA) recentes")
                + ". Não confirmado por sensor direto de temperatura por núcleo.";
        }

        if (clockDropUnderLoad)
        {
            return "Queda de frequência sob carga detectada, sem confirmação por outro sinal; "
                + "pode ser plano de energia, limite de potência ou throttling não confirmado.";
        }

        if (wheaSignal)
        {
            return "Eventos de erro de hardware (WHEA) recentes foram encontrados, sem sinal de "
                + "queda de frequência no momento desta leitura.";
        }

        return "Nenhum sinal de throttling foi detectado no momento desta leitura.";
    }
}

public sealed class ResourceUsageDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IResourceUsageInspector inspector;

    public ResourceUsageDiagnosisAction(IResourceUsageInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseResourceUsage);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(ResourceUsageSnapshot snapshot)
    {
        var network = snapshot.NetworkThroughputMBps.ToString("0.##", CultureInfo.InvariantCulture);
        return $"Uso no momento da leitura — CPU: {FormatPercent(snapshot.CpuPercent)}, "
            + $"disco: {FormatPercent(snapshot.DiskPercent)}, GPU: {FormatPercent(snapshot.GpuPercent)}, "
            + $"rede: {network} MB/s. Amostra instantânea, não uma média.";
    }

    private static string FormatPercent(double? value)
    {
        return value is { } percent
            ? percent.ToString("0", CultureInfo.InvariantCulture) + "%"
            : "não disponível";
    }
}

public sealed class PciLinkDiagnosisAction : ReadOnlyDiagnosticAction
{
    private readonly IPciLinkInspector inspector;

    public PciLinkDiagnosisAction(IPciLinkInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnosePciLink);

    protected override string Describe() => Classify(inspector.GetSnapshot());

    internal static string Classify(IReadOnlyList<PciLinkSnapshot> adapters)
    {
        var withData = adapters.Where(adapter =>
            adapter.CurrentLinkWidth is not null || adapter.CurrentLinkSpeedGtPerSecondTimesTen is not null).ToArray();

        if (withData.Length == 0)
        {
            return "A largura/velocidade do link PCIe da GPU não pôde ser lida de forma confiável "
                + "sem ferramenta do fabricante neste computador.";
        }

        var parts = withData.Select(adapter =>
        {
            var current = FormatLink(adapter.CurrentLinkWidth, adapter.CurrentLinkSpeedGtPerSecondTimesTen);
            var max = FormatLink(adapter.MaxLinkWidth, adapter.MaxLinkSpeedGtPerSecondTimesTen);
            return $"{adapter.AdapterName}: atual {current} de máximo {max}";
        });

        return string.Join("; ", parts) + ".";
    }

    private static string FormatLink(int? width, int? speedTimesTen)
    {
        var widthLabel = width is { } w ? $"x{w}" : "x?";
        var speedLabel = speedTimesTen is { } s ? $"{s / 10d:0.#} GT/s" : "?";
        return $"{widthLabel} @ {speedLabel}";
    }
}

public sealed class HardwareStabilityDiagnosisAction : ReadOnlyDiagnosticAction
{
    private const int OldBiosThresholdYears = 3;

    private readonly IHardwareStabilityInspector inspector;

    public HardwareStabilityDiagnosisAction(IHardwareStabilityInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseHardwareStability);

    protected override string Describe() => Classify(inspector.GetSnapshot(), DateTimeOffset.UtcNow);

    internal static string Classify(HardwareStabilitySnapshot snapshot, DateTimeOffset nowUtc)
    {
        var biosLabel = snapshot.BiosReleaseDateUtc is { } releaseDate
            ? BuildBiosLabel(releaseDate, nowUtc)
            : "Não foi possível ler a data de lançamento da BIOS.";

        var memoryLabel = snapshot.RecentMemoryFlavoredWheaEventCount > 0
            ? $"{snapshot.RecentMemoryFlavoredWheaEventCount} evento(s) de erro de hardware "
                + "possivelmente relacionados à memória nos últimos 30 dias."
            : "Nenhum evento de erro de hardware relacionado à memória nos últimos 30 dias.";

        return $"{biosLabel} {memoryLabel} Resizable BAR/Above 4G Decoding/Smart Access Memory não podem "
            + "ser detectados de forma confiável sem ferramenta do fabricante; verifique na BIOS ou no "
            + "painel oficial da placa-mãe/GPU.";
    }

    private static string BuildBiosLabel(DateTimeOffset releaseDate, DateTimeOffset nowUtc)
    {
        var ageYears = (nowUtc - releaseDate).TotalDays / 365.25;
        return ageYears >= OldBiosThresholdYears
            ? $"BIOS lançada em {releaseDate:yyyy-MM-dd}, com mais de {OldBiosThresholdYears} anos; "
                + "considere verificar atualizações no site do fabricante da placa-mãe."
            : $"BIOS lançada em {releaseDate:yyyy-MM-dd}, relativamente recente.";
    }
}

/// <summary>
/// The nine-way bottleneck classification requested for the benchmark/
/// comparison feature. It only reuses signals already read by the other
/// diagnostics in this file — no new system access beyond the background
/// process CPU reader — and returns the first category whose threshold
/// fires, in the priority order documented in <see cref="Classify"/>.
/// "Servidor limitado" is a conclusion by elimination (no local signal
/// stood out), never a direct measurement of the FiveM server.
/// </summary>
public sealed class BottleneckClassificationAction : ReadOnlyDiagnosticAction
{
    private const double HighUtilizationPercent = 90d;
    private const double ModerateUtilizationPercent = 85d;
    private const double BackgroundProcessCpuThresholdPercent = 20d;
    private const long SmallVramBytes = 4L * DiagnosticSignals.GiB;

    private static readonly IReadOnlyCollection<string> ExcludedProcessNames =
    [
        "FiveM", "FiveM_", "CitizenFX", "CitizenFX_", "GTA5", "GTA5_Enhanced",
        "FiveMCleaner", "FiveMCleaner.Broker"
    ];

    private readonly ISystemResourceInspector systemResources;
    private readonly IResourceUsageInspector resourceUsage;
    private readonly IThermalInspector thermal;
    private readonly INetworkHealthInspector networkHealth;
    private readonly IGpuDetailsInspector gpuDetails;
    private readonly IBackgroundProcessInspector backgroundProcess;

    public BottleneckClassificationAction(
        ISystemResourceInspector systemResources,
        IResourceUsageInspector resourceUsage,
        IThermalInspector thermal,
        INetworkHealthInspector networkHealth,
        IGpuDetailsInspector gpuDetails,
        IBackgroundProcessInspector backgroundProcess)
    {
        this.systemResources = systemResources ?? throw new ArgumentNullException(nameof(systemResources));
        this.resourceUsage = resourceUsage ?? throw new ArgumentNullException(nameof(resourceUsage));
        this.thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
        this.networkHealth = networkHealth ?? throw new ArgumentNullException(nameof(networkHealth));
        this.gpuDetails = gpuDetails ?? throw new ArgumentNullException(nameof(gpuDetails));
        this.backgroundProcess = backgroundProcess ?? throw new ArgumentNullException(nameof(backgroundProcess));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ClassifyBottleneck);

    protected override string Describe()
    {
        return Classify(new BottleneckClassificationInput(
            systemResources.GetSnapshot(),
            resourceUsage.GetSnapshot(),
            thermal.GetSnapshot(),
            networkHealth.GetSnapshot(),
            gpuDetails.GetSnapshot(),
            backgroundProcess.GetTopConsumer(ExcludedProcessNames)));
    }

    internal static string Classify(BottleneckClassificationInput input)
    {
        var logicalProcessors = Math.Max(1, input.SystemResources.LogicalProcessorCount);

        // 1. Térmico: alta temperatura disponível é o sinal mais direto que temos.
        if (DiagnosticSignals.IsTemperatureElevated(input.Thermal))
        {
            return $"Gargalo provável: térmico. Temperatura em ~{input.Thermal.HighestCelsius:0}°C, "
                + "o que pode causar throttling e queda de desempenho sob carga.";
        }

        // 2. Processo de fundo: outro processo consumindo CPU de forma relevante.
        if (input.BackgroundProcess is { } process
            && process.CpuPercent / logicalProcessors >= BackgroundProcessCpuThresholdPercent)
        {
            return $"Gargalo provável: processo em segundo plano. '{process.ProcessName}' está consumindo "
                + "CPU de forma relevante além do FiveM/GTA V.";
        }

        // 3. Rede: perda/erro de pacotes local.
        if (input.NetworkHealth.HasActiveInterface
            && (input.NetworkHealth.DiscardedPackets > 0 || input.NetworkHealth.ErrorPackets > 0))
        {
            return "Gargalo provável: rede. Há descarte ou erro de pacotes na placa de rede ativa, "
                + "o que pode causar jitter ou perda de conexão com o servidor.";
        }

        // 4. Disco: tempo ativo elevado.
        if (input.ResourceUsage.DiskPercent >= HighUtilizationPercent)
        {
            return "Gargalo provável: disco. A unidade está com tempo ativo elevado, "
                + "o que pode causar travamentos ao carregar texturas/streaming.";
        }

        // 5. RAM: pouca memória disponível.
        if (DiagnosticSignals.IsMemoryUnderPressure(input.SystemResources))
        {
            return "Gargalo provável: memória RAM. A memória disponível está baixa, "
                + "o que pode causar paginação e engasgos.";
        }

        // 6. VRAM: GPU saturada em uma placa com pouca VRAM total (estimativa).
        var lowestVram = input.GpuDetails
            .Where(gpu => gpu.VramBytes is > 0)
            .Select(gpu => gpu.VramBytes!.Value)
            .DefaultIfEmpty(0)
            .Min();
        if (input.ResourceUsage.GpuPercent >= HighUtilizationPercent
            && lowestVram is > 0 and <= SmallVramBytes)
        {
            return "Gargalo provável: VRAM. A GPU detectada tem pouca memória de vídeo (4 GB ou menos) "
                + "e está com uso alto; texturas em qualidade mais alta podem causar stutter.";
        }

        // 7. GPU: GPU saturada com CPU folgada.
        if (input.ResourceUsage.GpuPercent >= HighUtilizationPercent
            && input.ResourceUsage.CpuPercent < ModerateUtilizationPercent)
        {
            return "Gargalo provável: GPU. A GPU está próxima do limite enquanto a CPU ainda tem folga; "
                + "reduzir opções gráficas tende a ajudar mais que ajustes de CPU.";
        }

        // 8. CPU: CPU saturada com GPU não saturada.
        if (input.ResourceUsage.CpuPercent >= ModerateUtilizationPercent
            && input.ResourceUsage.GpuPercent < HighUtilizationPercent)
        {
            return "Gargalo provável: CPU. A CPU está com uso alto enquanto a GPU tem folga; "
                + "reduzir opções gráficas tende a ajudar pouco nesse caso.";
        }

        // 9. Servidor (por eliminação): nenhum sinal local se destacou.
        return "Nenhum gargalo local evidente foi encontrado; se o desempenho ainda estiver ruim, "
            + "os recursos do servidor FiveM (scripts e mapas) podem ser o fator limitante.";
    }
}

public sealed record BottleneckClassificationInput(
    SystemResourceSnapshot SystemResources,
    ResourceUsageSnapshot ResourceUsage,
    ThermalSnapshot Thermal,
    NetworkHealthSnapshot NetworkHealth,
    IReadOnlyList<GpuAdapterDetails> GpuDetails,
    BackgroundProcessUsage? BackgroundProcess);
