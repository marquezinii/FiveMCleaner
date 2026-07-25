using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class HardwareDiagnosticActionsTests
{
    [Fact]
    public void CpuDetails_ReportsHonestlyWhenUnavailable()
    {
        Assert.Contains("Não foi possível ler", CpuDetailsDiagnosisAction.Classify(null), StringComparison.Ordinal);
    }

    [Fact]
    public void CpuDetails_FlagsSignificantClockDrop()
    {
        var message = CpuDetailsDiagnosisAction.Classify(new CpuSnapshot(8, 16, 1000, 4800));

        Assert.Contains("núcleo(s)", message, StringComparison.Ordinal);
        Assert.Contains("bem abaixo do máximo", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CpuDetails_DoesNotFlagNormalClock()
    {
        var message = CpuDetailsDiagnosisAction.Classify(new CpuSnapshot(8, 16, 4200, 4800));

        Assert.DoesNotContain("bem abaixo do máximo", message, StringComparison.Ordinal);
    }

    [Fact]
    public void GpuDetails_ReportsWhenNothingFound()
    {
        Assert.Contains("Não foi possível detectar", GpuDetailsDiagnosisAction.Classify([]), StringComparison.Ordinal);
    }

    [Fact]
    public void GpuDetails_ReportsVramAndKindGuess()
    {
        var message = GpuDetailsDiagnosisAction.Classify(
        [
            new GpuAdapterDetails("NVIDIA GeForce RTX 4070", 12L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete),
            new GpuAdapterDetails("Intel(R) UHD Graphics 770", null, GpuKindGuess.LikelyIntegrated)
        ]);

        Assert.Contains("12 GB de VRAM", message, StringComparison.Ordinal);
        Assert.Contains("provavelmente dedicada", message, StringComparison.Ordinal);
        Assert.Contains("VRAM não detectada", message, StringComparison.Ordinal);
        Assert.Contains("provavelmente integrada", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RamDetails_ReportsHonestlyWhenNoModulesFound()
    {
        Assert.Contains("Não foi possível ler", RamDetailsDiagnosisAction.Classify(new RamDetailsSnapshot([])), StringComparison.Ordinal);
    }

    [Fact]
    public void RamDetails_FlagsSingleChannelWithOneModule()
    {
        var snapshot = new RamDetailsSnapshot(
        [
            new RamModuleInfo(16L * 1024 * 1024 * 1024, 3200, 3200, "DIMM0")
        ]);

        var message = RamDetailsDiagnosisAction.Classify(snapshot);

        Assert.Contains("single-channel", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RamDetails_FlagsLikelyDisabledXmpWhenConfiguredIsBelowRated()
    {
        var snapshot = new RamDetailsSnapshot(
        [
            new RamModuleInfo(16L * 1024 * 1024 * 1024, 2133, 3600, "DIMM0"),
            new RamModuleInfo(16L * 1024 * 1024 * 1024, 2133, 3600, "DIMM1")
        ]);

        var message = RamDetailsDiagnosisAction.Classify(snapshot);

        Assert.Contains("multi-channel", message, StringComparison.Ordinal);
        Assert.Contains("possivelmente desativado", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RamDetails_ReportsXmpLikelyActiveWhenConfiguredMeetsRated()
    {
        var snapshot = new RamDetailsSnapshot(
        [
            new RamModuleInfo(16L * 1024 * 1024 * 1024, 3600, 3600, "DIMM0"),
            new RamModuleInfo(16L * 1024 * 1024 * 1024, 3600, 3600, "DIMM1")
        ]);

        var message = RamDetailsDiagnosisAction.Classify(snapshot);

        Assert.Contains("provavelmente ativo", message, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageHealth_ReportsHonestlyWhenNoDisksFound()
    {
        Assert.Contains("Não foi possível ler", StorageHealthDiagnosisAction.Classify(new StorageHealthSnapshot([])), StringComparison.Ordinal);
    }

    [Fact]
    public void StorageHealth_FlagsUnhealthyDisks()
    {
        var snapshot = new StorageHealthSnapshot(
        [
            new PhysicalDiskInfo("NVMe SSD 1TB", "SSD/NVMe", true, "Saudável"),
            new PhysicalDiskInfo("Old HDD", "HDD", false, "Aviso")
        ]);

        var message = StorageHealthDiagnosisAction.Classify(snapshot);

        Assert.Contains("Atenção: 1 unidade(s)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageHealth_ReportsAllHealthyWhenNoIssues()
    {
        var snapshot = new StorageHealthSnapshot(
        [
            new PhysicalDiskInfo("NVMe SSD 1TB", "SSD/NVMe", true, "Saudável")
        ]);

        var message = StorageHealthDiagnosisAction.Classify(snapshot);

        Assert.Contains("saúde normal", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverVersions_ReportsHonestlyWhenNothingFound()
    {
        var message = DriverVersionsDiagnosisAction.Classify(new DriverVersionSnapshot([], [], [], []));

        Assert.Contains("Não foi possível ler", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverVersions_GroupsByDeviceClass()
    {
        var snapshot = new DriverVersionSnapshot(
            Video: [new DriverVersionInfo("NVIDIA GeForce RTX 4070", "32.0.15.6094")],
            Network: [new DriverVersionInfo("Realtek Ethernet", "10.55.0.1")],
            Audio: [],
            Chipset: []);

        var message = DriverVersionsDiagnosisAction.Classify(snapshot);

        Assert.Contains("Vídeo:", message, StringComparison.Ordinal);
        Assert.Contains("Rede:", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Áudio:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayConfiguration_ReportsHonestlyWhenUnavailable()
    {
        Assert.Contains("Não foi possível ler", DisplayConfigurationDiagnosisAction.Classify(null), StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayConfiguration_FlagsBelowMaximumRefreshRate()
    {
        var snapshot = new DisplayConfigurationSnapshot(1920, 1080, 60, 144, HardwareGpuSchedulingState.Enabled);

        var message = DisplayConfigurationDiagnosisAction.Classify(snapshot);

        Assert.Contains("abaixo da máxima suportada", message, StringComparison.Ordinal);
        Assert.Contains("HAGS", message, StringComparison.Ordinal);
        Assert.Contains("ativado", message, StringComparison.Ordinal);
        Assert.Contains("G-SYNC/FreeSync/VRR não podem ser", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayConfiguration_DoesNotFlagWhenAtMaximum()
    {
        var snapshot = new DisplayConfigurationSnapshot(1920, 1080, 144, 144, HardwareGpuSchedulingState.Disabled);

        var message = DisplayConfigurationDiagnosisAction.Classify(snapshot);

        Assert.DoesNotContain("abaixo da máxima suportada", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "ativado")]
    [InlineData(false, "desativado")]
    public void SessionSettings_ClassifiesGameModeState(bool enabled, string expectedSubstring)
    {
        var gameMode = enabled ? RegistryValueState.FromDword(1) : RegistryValueState.FromDword(0);
        var message = SessionSettingsDiagnosisAction.Classify(
            gameMode, RegistryValueState.Missing, "Balanceado");

        Assert.Contains(expectedSubstring, message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionSettings_ClassifiesFullscreenOptimizationsDisabled()
    {
        var message = SessionSettingsDiagnosisAction.Classify(
            RegistryValueState.Missing, RegistryValueState.FromDword(2), "Alto desempenho");

        Assert.Contains("desativadas", message, StringComparison.Ordinal);
        Assert.Contains("Alto desempenho", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrottlingSignal_CombinesClockDropAndTemperature()
    {
        var cpu = new CpuSnapshot(8, 16, 1000, 4800);
        var usage = new ResourceUsageSnapshot(80, null, null, 0);
        var stability = new HardwareStabilitySnapshot(0, 0, null);
        var thermal = new ThermalSnapshot(true, 92);

        var message = ThrottlingSignalDiagnosisAction.Classify(cpu, usage, stability, thermal);

        Assert.Contains("Possível throttling detectado", message, StringComparison.Ordinal);
        Assert.Contains("temperatura elevada", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrottlingSignal_CombinesClockDropAndWheaWhenNoThermalData()
    {
        var cpu = new CpuSnapshot(8, 16, 1000, 4800);
        var usage = new ResourceUsageSnapshot(80, null, null, 0);
        var stability = new HardwareStabilitySnapshot(3, 0, null);
        var thermal = new ThermalSnapshot(false, null);

        var message = ThrottlingSignalDiagnosisAction.Classify(cpu, usage, stability, thermal);

        Assert.Contains("Possível throttling detectado", message, StringComparison.Ordinal);
        Assert.Contains("WHEA", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrottlingSignal_ReportsUnconfirmedClockDropAlone()
    {
        var cpu = new CpuSnapshot(8, 16, 1000, 4800);
        var usage = new ResourceUsageSnapshot(80, null, null, 0);
        var stability = new HardwareStabilitySnapshot(0, 0, null);
        var thermal = new ThermalSnapshot(false, null);

        var message = ThrottlingSignalDiagnosisAction.Classify(cpu, usage, stability, thermal);

        Assert.Contains("Queda de frequência sob carga detectada", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrottlingSignal_ReportsNoSignalWhenHealthy()
    {
        var cpu = new CpuSnapshot(8, 16, 4700, 4800);
        var usage = new ResourceUsageSnapshot(20, null, null, 0);
        var stability = new HardwareStabilitySnapshot(0, 0, null);
        var thermal = new ThermalSnapshot(true, 55);

        var message = ThrottlingSignalDiagnosisAction.Classify(cpu, usage, stability, thermal);

        Assert.Contains("Nenhum sinal de throttling", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceUsage_ReportsUnavailableCountersHonestly()
    {
        var message = ResourceUsageDiagnosisAction.Classify(new ResourceUsageSnapshot(null, null, null, 0));

        Assert.Contains("CPU: não disponível", message, StringComparison.Ordinal);
        Assert.Contains("GPU: não disponível", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceUsage_FormatsAvailableValues()
    {
        var message = ResourceUsageDiagnosisAction.Classify(new ResourceUsageSnapshot(42, 10, 5, 3.25));

        Assert.Contains("CPU: 42%", message, StringComparison.Ordinal);
        Assert.Contains("3.25 MB/s", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PciLink_ReportsHonestlyWhenNoDataAvailable()
    {
        Assert.Contains("não pôde ser lida", PciLinkDiagnosisAction.Classify([]), StringComparison.Ordinal);
    }

    [Fact]
    public void PciLink_FormatsAvailableWidthAndSpeed()
    {
        var snapshot = new PciLinkSnapshot("NVIDIA GeForce RTX 4070", 16, 80, 16, 160);

        var message = PciLinkDiagnosisAction.Classify([snapshot]);

        Assert.Contains("x16", message, StringComparison.Ordinal);
        Assert.Contains("8 GT/s", message, StringComparison.Ordinal);
        Assert.Contains("16 GT/s", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareStability_FlagsOldBios()
    {
        var snapshot = new HardwareStabilitySnapshot(0, 0, new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var message = HardwareStabilityDiagnosisAction.Classify(
            snapshot, new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("com mais de 3 anos", message, StringComparison.Ordinal);
        Assert.Contains("Resizable BAR", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareStability_ReportsRecentBiosAsFine()
    {
        var snapshot = new HardwareStabilitySnapshot(0, 0, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var message = HardwareStabilityDiagnosisAction.Classify(
            snapshot, new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("relativamente recente", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareStability_FlagsMemoryFlavoredWheaEvents()
    {
        var snapshot = new HardwareStabilitySnapshot(5, 2, null);

        var message = HardwareStabilityDiagnosisAction.Classify(snapshot, DateTimeOffset.UtcNow);

        Assert.Contains("2 evento(s)", message, StringComparison.Ordinal);
    }
}

public sealed class BottleneckClassificationActionTests
{
    private static readonly SystemResourceSnapshot HealthyResources = new(
        TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
        AvailableMemoryBytes: 8L * 1024 * 1024 * 1024,
        LogicalProcessorCount: 12,
        SystemDriveFreeBytes: 100L * 1024 * 1024 * 1024,
        TotalPageFileBytes: 20L * 1024 * 1024 * 1024,
        AvailablePageFileBytes: 16L * 1024 * 1024 * 1024);

    private static readonly ResourceUsageSnapshot HealthyUsage = new(30, 10, 40, 1.0);
    private static readonly ThermalSnapshot NoThermalData = new(false, null);
    private static readonly NetworkHealthSnapshot HealthyNetwork = new(true, 0, 0);
    private static readonly IReadOnlyList<GpuAdapterDetails> BigVramGpu =
        [new GpuAdapterDetails("NVIDIA GeForce RTX 4070", 12L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete)];

    [Fact]
    public void Classify_PrioritizesThermalWhenTemperatureIsElevated()
    {
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage, new ThermalSnapshot(true, 90), HealthyNetwork, BigVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("térmico", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FlagsBackgroundProcessConsumingCpu()
    {
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage, NoThermalData, HealthyNetwork, BigVramGpu,
            new BackgroundProcessUsage("chrome", 400)); // 400% / 12 cores ≈ 33%, above threshold

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("processo em segundo plano", message, StringComparison.Ordinal);
        Assert.Contains("chrome", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FlagsNetworkWhenPacketsAreDiscarded()
    {
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage, NoThermalData, new NetworkHealthSnapshot(true, 5, 0), BigVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("rede", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FlagsDiskWhenDiskTimeIsHigh()
    {
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage with { DiskPercent = 95 }, NoThermalData, HealthyNetwork, BigVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("disco", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FlagsRamWhenAvailableIsLow()
    {
        var lowMemory = HealthyResources with { AvailableMemoryBytes = 512L * 1024 * 1024 };
        var input = new BottleneckClassificationInput(
            lowMemory, HealthyUsage, NoThermalData, HealthyNetwork, BigVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("memória RAM", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FlagsVramWhenGpuIsSaturatedAndVramIsSmall()
    {
        IReadOnlyList<GpuAdapterDetails> smallVramGpu =
            [new GpuAdapterDetails("Old GPU", 2L * 1024 * 1024 * 1024, GpuKindGuess.LikelyDiscrete)];
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage with { GpuPercent = 98 }, NoThermalData, HealthyNetwork, smallVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("VRAM", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FlagsGpuWhenSaturatedWithCpuHeadroom()
    {
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage with { GpuPercent = 98, CpuPercent = 40 }, NoThermalData, HealthyNetwork, BigVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("Gargalo provável: GPU", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FlagsCpuWhenSaturatedWithGpuHeadroom()
    {
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage with { CpuPercent = 95, GpuPercent = 40 }, NoThermalData, HealthyNetwork, BigVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("Gargalo provável: CPU", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_FallsBackToServerByEliminationWhenNothingElseFires()
    {
        var input = new BottleneckClassificationInput(
            HealthyResources, HealthyUsage, NoThermalData, HealthyNetwork, BigVramGpu, null);

        var message = BottleneckClassificationAction.Classify(input);

        Assert.Contains("servidor FiveM", message, StringComparison.Ordinal);
    }
}

public sealed class HardwareInspectorSmokeTests
{
    [Fact]
    public void WindowsCpuInspector_NeverThrows()
    {
        var snapshot = new WindowsCpuInspector().GetSnapshot();
        if (snapshot is not null)
        {
            Assert.True(snapshot.PhysicalCores > 0);
            Assert.True(snapshot.LogicalThreads > 0);
        }
    }

    [Fact]
    public void WindowsGpuDetailsInspector_NeverThrows()
    {
        Assert.NotNull(new WindowsGpuDetailsInspector().GetSnapshot());
    }

    [Fact]
    public void WindowsRamDetailsInspector_NeverThrows()
    {
        Assert.NotNull(new WindowsRamDetailsInspector().GetSnapshot().Modules);
    }

    [Fact]
    public void WindowsStorageHealthInspector_NeverThrows()
    {
        Assert.NotNull(new WindowsStorageHealthInspector().GetSnapshot().Disks);
    }

    [Fact]
    public void WindowsDriverVersionInspector_NeverThrows()
    {
        var snapshot = new WindowsDriverVersionInspector().GetSnapshot();
        Assert.NotNull(snapshot.Video);
        Assert.NotNull(snapshot.Network);
        Assert.NotNull(snapshot.Audio);
        Assert.NotNull(snapshot.Chipset);
    }

    [Fact]
    public void WindowsDisplayConfigurationInspector_NeverThrows()
    {
        var snapshot = new WindowsDisplayConfigurationInspector().GetSnapshot();
        if (snapshot is not null)
        {
            Assert.True(snapshot.Width > 0);
            Assert.True(snapshot.Height > 0);
            Assert.True(snapshot.CurrentRefreshHz > 0);
        }
    }

    [Fact]
    public void WindowsResourceUsageInspector_NeverThrows()
    {
        Assert.NotNull(new WindowsResourceUsageInspector().GetSnapshot());
    }

    [Fact]
    public void WindowsPciLinkInspector_NeverThrows()
    {
        Assert.NotNull(new WindowsPciLinkInspector().GetSnapshot());
    }

    [Fact]
    public void WindowsHardwareStabilityInspector_NeverThrows()
    {
        Assert.NotNull(new WindowsHardwareStabilityInspector().GetSnapshot());
    }

    [Fact]
    public void WindowsBackgroundProcessInspector_NeverThrows()
    {
        // May legitimately return null when nothing exceeds the internal
        // exclusions, so only the absence of an exception is asserted here.
        _ = new WindowsBackgroundProcessInspector().GetTopConsumer(["FiveMCleaner"]);
    }
}
