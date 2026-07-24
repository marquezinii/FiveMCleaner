using System.Runtime.InteropServices;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Read-only hardware/system diagnostics from the TERCEIRA FASE request.
/// Every action here always resolves to <see cref="WindowsActionApplyResult.NoChange"/>
/// with an honest message; none of them ever writes to the system, installs
/// a driver, or claims data it could not actually read.
/// </summary>
public sealed class CpuDetailsDiagnosisAction : WindowsOptimizationAction
{
    private readonly ICpuInspector inspector;

    public CpuDetailsDiagnosisAction(ICpuInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseCpuDetails);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(inspector.GetSnapshot())));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(CpuSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "Não foi possível ler os detalhes da CPU neste momento.";
        }

        var message = $"{snapshot.PhysicalCores} núcleo(s) físico(s), {snapshot.LogicalThreads} thread(s) lógica(s). "
            + $"Frequência atual: {snapshot.CurrentClockMhz} MHz de {snapshot.MaxClockMhz} MHz máximos.";
        if (snapshot.MaxClockMhz > 0 && snapshot.CurrentClockMhz < snapshot.MaxClockMhz * 0.5)
        {
            message += " A frequência atual está bem abaixo do máximo (economia de energia ou possível throttling).";
        }

        return message;
    }
}

public sealed class GpuDetailsDiagnosisAction : WindowsOptimizationAction
{
    private readonly IGpuDetailsInspector inspector;

    public GpuDetailsDiagnosisAction(IGpuDetailsInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseGpuDetails);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(inspector.GetSnapshot())));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(IReadOnlyList<GpuAdapterDetails> adapters)
    {
        if (adapters.Count == 0)
        {
            return "Não foi possível detectar detalhes de VRAM/tipo da GPU neste momento.";
        }

        var parts = adapters.Select(adapter =>
        {
            var vram = adapter.VramBytes is > 0
                ? $"{adapter.VramBytes.Value / (1024d * 1024d * 1024d):0.#} GB de VRAM"
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

public sealed class RamDetailsDiagnosisAction : WindowsOptimizationAction
{
    private readonly IRamDetailsInspector inspector;

    public RamDetailsDiagnosisAction(IRamDetailsInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseRamDetails);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(inspector.GetSnapshot())));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(RamDetailsSnapshot snapshot)
    {
        if (snapshot.Modules.Count == 0)
        {
            return "Não foi possível ler os detalhes dos módulos de memória neste momento.";
        }

        var count = snapshot.Modules.Count;
        var configured = snapshot.Modules
            .Where(module => module.ConfiguredClockMhz > 0)
            .Select(module => module.ConfiguredClockMhz)
            .DefaultIfEmpty(0u)
            .Max();

        var channelHint = count == 1
            ? "provavelmente single-channel (apenas um pente instalado)"
            : count % 2 == 0
                ? "provavelmente multi-channel (quantidade par de pentes)"
                : "quantidade ímpar de pentes; a configuração de canais não pôde ser confirmada";

        var xmpHint = BuildXmpHint(snapshot.Modules);

        var frequencyLabel = configured > 0
            ? $"{configured} MHz configurados"
            : "frequência configurada não disponível";

        return $"{count} módulo(s) de memória detectado(s), {frequencyLabel}. {channelHint}. {xmpHint}";
    }

    private static string BuildXmpHint(IReadOnlyList<RamModuleInfo> modules)
    {
        var withRated = modules.Where(module => module.RatedClockMhz > 0 && module.ConfiguredClockMhz > 0).ToArray();
        if (withRated.Length == 0)
        {
            return "Não foi possível comparar a velocidade configurada com a velocidade nominal (XMP/EXPO).";
        }

        var belowRated = withRated.Any(module => module.ConfiguredClockMhz < module.RatedClockMhz * 0.9);
        return belowRated
            ? "A memória parece rodar abaixo da velocidade nominal (XMP/EXPO possivelmente desativado)."
            : "A memória parece rodar na velocidade nominal ou acima (XMP/EXPO provavelmente ativo).";
    }
}

public sealed class StorageHealthDiagnosisAction : WindowsOptimizationAction
{
    private readonly IStorageHealthInspector inspector;

    public StorageHealthDiagnosisAction(IStorageHealthInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseStorageHealth);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(inspector.GetSnapshot())));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(StorageHealthSnapshot snapshot)
    {
        if (snapshot.Disks.Count == 0)
        {
            return "Não foi possível ler o tipo/saúde das unidades físicas neste momento.";
        }

        var unhealthy = snapshot.Disks.Where(disk => !disk.IsHealthy).ToArray();
        var summary = string.Join("; ", snapshot.Disks.Select(disk =>
            $"{disk.FriendlyName} ({disk.MediaTypeLabel}, {disk.HealthStatusLabel})"));

        return unhealthy.Length > 0
            ? $"Atenção: {unhealthy.Length} unidade(s) com alerta de saúde. Unidades detectadas: {summary}."
            : $"Todas as unidades detectadas relatam saúde normal. Unidades: {summary}.";
    }
}

public sealed class DriverVersionsDiagnosisAction : WindowsOptimizationAction
{
    private readonly IDriverVersionInspector inspector;

    public DriverVersionsDiagnosisAction(IDriverVersionInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseDriverVersions);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buildLabel = OperatingSystem.IsWindows()
            ? $"build {Environment.OSVersion.Version.Build}"
            : "build desconhecido";
        return Task.FromResult(WindowsActionApplyResult.NoChange(
            $"Windows {buildLabel}. {Classify(inspector.GetSnapshot())}"));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

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
}

public sealed class DisplayConfigurationDiagnosisAction : WindowsOptimizationAction
{
    private readonly IDisplayConfigurationInspector inspector;

    public DisplayConfigurationDiagnosisAction(IDisplayConfigurationInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseDisplayConfiguration);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(inspector.GetSnapshot())));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

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

public sealed class SessionSettingsDiagnosisAction : WindowsOptimizationAction
{
    private static readonly RegistryAddress GameModeAddress = new(
        Microsoft.Win32.RegistryHive.CurrentUser,
        @"Software\Microsoft\GameBar",
        "AutoGameModeEnabled");

    private static readonly RegistryAddress FullscreenOptimizationsAddress = new(
        Microsoft.Win32.RegistryHive.CurrentUser,
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

public sealed class ThrottlingSignalDiagnosisAction : WindowsOptimizationAction
{
    private const double ElevatedTemperatureCelsius = 85d;
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

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpuSnapshot = cpu.GetSnapshot();
        var usageSnapshot = usage.GetSnapshot();
        var stabilitySnapshot = stability.GetSnapshot();
        var thermalSnapshot = thermal.GetSnapshot();
        return Task.FromResult(WindowsActionApplyResult.NoChange(
            Classify(cpuSnapshot, usageSnapshot, stabilitySnapshot, thermalSnapshot)));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(
        CpuSnapshot? cpuSnapshot,
        ResourceUsageSnapshot usageSnapshot,
        HardwareStabilitySnapshot stabilitySnapshot,
        ThermalSnapshot thermalSnapshot)
    {
        var clockDropUnderLoad = cpuSnapshot is not null
            && cpuSnapshot.MaxClockMhz > 0
            && cpuSnapshot.CurrentClockMhz < cpuSnapshot.MaxClockMhz * 0.6
            && usageSnapshot.CpuPercent is > 50;
        var wheaSignal = stabilitySnapshot.RecentWheaEventCount > 0;
        var thermalSignal = thermalSnapshot is { IsAvailable: true, HighestCelsius: >= ElevatedTemperatureCelsius };

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

public sealed class ResourceUsageDiagnosisAction : WindowsOptimizationAction
{
    private readonly IResourceUsageInspector inspector;

    public ResourceUsageDiagnosisAction(IResourceUsageInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseResourceUsage);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(inspector.GetSnapshot())));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string Classify(ResourceUsageSnapshot snapshot)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var cpu = snapshot.CpuPercent is { } cpuValue ? cpuValue.ToString("0", culture) + "%" : "não disponível";
        var disk = snapshot.DiskPercent is { } diskValue ? diskValue.ToString("0", culture) + "%" : "não disponível";
        var gpu = snapshot.GpuPercent is { } gpuValue ? gpuValue.ToString("0", culture) + "%" : "não disponível";
        var network = snapshot.NetworkThroughputMBps.ToString("0.##", culture);
        return $"Uso no momento da leitura — CPU: {cpu}, disco: {disk}, GPU: {gpu}, "
            + $"rede: {network} MB/s. Amostra instantânea, não uma média.";
    }
}

public sealed class PciLinkDiagnosisAction : WindowsOptimizationAction
{
    private readonly IPciLinkInspector inspector;

    public PciLinkDiagnosisAction(IPciLinkInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnosePciLink);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(Classify(inspector.GetSnapshot())));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

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

public sealed class HardwareStabilityDiagnosisAction : WindowsOptimizationAction
{
    private const int OldBiosThresholdYears = 3;
    private readonly IHardwareStabilityInspector inspector;

    public HardwareStabilityDiagnosisAction(IHardwareStabilityInspector inspector)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseHardwareStability);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WindowsActionApplyResult.NoChange(
            Classify(inspector.GetSnapshot(), DateTimeOffset.UtcNow)));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken) => Task.CompletedTask;

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
