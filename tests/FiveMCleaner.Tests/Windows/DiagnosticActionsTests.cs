using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class DiagnosticActionsTests
{
    [Fact]
    public void BottleneckDiagnosis_ClassifiesLowAvailableMemoryFirst()
    {
        var snapshot = new SystemResourceSnapshot(
            TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 512L * 1024 * 1024,
            LogicalProcessorCount: 16,
            SystemDriveFreeBytes: 100L * 1024 * 1024 * 1024,
            TotalPageFileBytes: 24L * 1024 * 1024 * 1024,
            AvailablePageFileBytes: 20L * 1024 * 1024 * 1024);

        var message = BottleneckDiagnosisAction.Classify(snapshot);

        Assert.Contains("memória RAM", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BottleneckDiagnosis_ClassifiesLowProcessorCountWhenMemoryIsFine()
    {
        var snapshot = new SystemResourceSnapshot(
            TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 10L * 1024 * 1024 * 1024,
            LogicalProcessorCount: 2,
            SystemDriveFreeBytes: 100L * 1024 * 1024 * 1024,
            TotalPageFileBytes: 24L * 1024 * 1024 * 1024,
            AvailablePageFileBytes: 20L * 1024 * 1024 * 1024);

        var message = BottleneckDiagnosisAction.Classify(snapshot);

        Assert.Contains("processadores lógicos", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BottleneckDiagnosis_ClassifiesLowDiskWhenMemoryAndCpuAreFine()
    {
        var snapshot = new SystemResourceSnapshot(
            TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 10L * 1024 * 1024 * 1024,
            LogicalProcessorCount: 16,
            SystemDriveFreeBytes: 2L * 1024 * 1024 * 1024,
            TotalPageFileBytes: 24L * 1024 * 1024 * 1024,
            AvailablePageFileBytes: 20L * 1024 * 1024 * 1024);

        var message = BottleneckDiagnosisAction.Classify(snapshot);

        Assert.Contains("espaço livre em disco", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BottleneckDiagnosis_ReportsNoBottleneckWhenEverythingIsHealthy()
    {
        var snapshot = new SystemResourceSnapshot(
            TotalMemoryBytes: 32L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 20L * 1024 * 1024 * 1024,
            LogicalProcessorCount: 16,
            SystemDriveFreeBytes: 200L * 1024 * 1024 * 1024,
            TotalPageFileBytes: 24L * 1024 * 1024 * 1024,
            AvailablePageFileBytes: 20L * 1024 * 1024 * 1024);

        var message = BottleneckDiagnosisAction.Classify(snapshot);

        Assert.Contains("Nenhum gargalo evidente", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BottleneckDiagnosisAction_NeverWritesAndAlwaysCompletes()
    {
        var inspector = new FakeSystemResourceInspector();
        var action = new BottleneckDiagnosisAction(inspector);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Null(result.SnapshotJson);
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public async Task BottleneckDiagnosisAction_DegradesGracefullyWhenInspectorThrows()
    {
        var action = new BottleneckDiagnosisAction(new ThrowingSystemResourceInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains(result.Messages, message => message.Contains("Não foi possível", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverlaySoftwareDetectionAction_ReportsFoundOverlaysWithoutClosingThem()
    {
        var inspector = new FakeOverlaySoftwareInspector { Names = ["Overlay do Discord"] };
        var action = new OverlaySoftwareDetectionAction(inspector);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains(result.Messages, message => message.Contains("Overlay do Discord", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverlaySoftwareDetectionAction_ReportsNoneFoundWhenListIsEmpty()
    {
        var action = new OverlaySoftwareDetectionAction(new FakeOverlaySoftwareInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains(result.Messages, message => message.Contains("Nenhum overlay", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FiveMLegacyLogReaderAction_ReportsWhenNoLogsDirectoryExists()
    {
        using var temporary = new TemporaryDirectory();
        var action = new FiveMLegacyLogReaderAction(temporary.Combine("FiveM.app"));

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains(result.Messages, message => message.Contains("Nenhum log recente", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FiveMLegacyLogReaderAction_CountsPossibleErrorsInTheNewestLogOnly()
    {
        using var temporary = new TemporaryDirectory();
        var appRoot = temporary.Combine("FiveM.app");
        var logsDirectory = Path.Combine(appRoot, "logs");
        Directory.CreateDirectory(logsDirectory);

        var older = Path.Combine(logsDirectory, "old.log");
        File.WriteAllText(older, "error error error\nerror\n");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));

        var newer = Path.Combine(logsDirectory, "recent.log");
        File.WriteAllText(newer, "info: started\nerror: could not load resource\nok\n");
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var action = new FiveMLegacyLogReaderAction(appRoot);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        var message = Assert.Single(result.Messages);
        Assert.Contains("recent.log", message, StringComparison.Ordinal);
        Assert.Contains("1 linha(s)", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerformanceDiagnosticsGuideAction_ReferencesOfficialCommandsOnly()
    {
        var action = new PerformanceDiagnosticsGuideAction();

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        var message = Assert.Single(result.Messages);
        Assert.Contains("cl_drawfps", message, StringComparison.Ordinal);
        Assert.Contains("cl_drawperf", message, StringComparison.Ordinal);
        Assert.Contains("netgraph", message, StringComparison.Ordinal);
        Assert.Contains("resmon", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 0, 0, "Nenhum sinal local")]
    [InlineData(true, 5, 0, "Sinais locais de instabilidade")]
    [InlineData(true, 0, 3, "Sinais locais de instabilidade")]
    [InlineData(false, 0, 0, "Não foi possível ler estatísticas")]
    public void NetworkHealthDiagnosis_ClassifiesFromLocalCountersOnly(
        bool hasActiveInterface,
        long discarded,
        long errors,
        string expectedSubstring)
    {
        var snapshot = new NetworkHealthSnapshot(hasActiveInterface, discarded, errors);

        var message = NetworkHealthDiagnosisAction.Classify(snapshot);

        Assert.Contains(expectedSubstring, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NetworkHealthDiagnosisAction_NeverWrites()
    {
        var action = new NetworkHealthDiagnosisAction(new FakeNetworkHealthInspector());

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.NotEmpty(result.Messages);
    }

    [Fact]
    public void ThermalDiagnosis_ReportsHonestlyWhenUnavailable()
    {
        var message = ThermalDiagnosisAction.Classify(new ThermalSnapshot(false, null));

        Assert.Contains("não expõe uma leitura confiável", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(45d, "faixa normal")]
    [InlineData(92d, "throttling térmico")]
    public void ThermalDiagnosis_ClassifiesAvailableReadingByThreshold(double celsius, string expectedSubstring)
    {
        var message = ThermalDiagnosisAction.Classify(new ThermalSnapshot(true, celsius));

        Assert.Contains(expectedSubstring, message, StringComparison.Ordinal);
    }

    [Fact]
    public void PagefileCommitDiagnosis_WarnsWhenAvailableRatioIsLow()
    {
        var snapshot = new SystemResourceSnapshot(
            TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 8L * 1024 * 1024 * 1024,
            LogicalProcessorCount: 8,
            SystemDriveFreeBytes: 50L * 1024 * 1024 * 1024,
            TotalPageFileBytes: 20L * 1024 * 1024 * 1024,
            AvailablePageFileBytes: 1L * 1024 * 1024 * 1024);

        var message = PagefileCommitDiagnosisAction.Classify(snapshot);

        Assert.Contains("próximo do limite", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PagefileCommitDiagnosis_ReportsHealthyWhenThereIsHeadroom()
    {
        var snapshot = new SystemResourceSnapshot(
            TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
            AvailableMemoryBytes: 8L * 1024 * 1024 * 1024,
            LogicalProcessorCount: 8,
            SystemDriveFreeBytes: 50L * 1024 * 1024 * 1024,
            TotalPageFileBytes: 20L * 1024 * 1024 * 1024,
            AvailablePageFileBytes: 16L * 1024 * 1024 * 1024);

        var message = PagefileCommitDiagnosisAction.Classify(snapshot);

        Assert.Contains("folga suficiente", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CacheIndexIntegrityDiagnosisAction_ReportsNoIndexFoundWhenAbsent()
    {
        using var temporary = new TemporaryDirectory();
        var action = new CacheIndexIntegrityDiagnosisAction(temporary.Combine("FiveM.app"));

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains(result.Messages, message => message.Contains("Nenhum índice de cache", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CacheIndexIntegrityDiagnosisAction_DetectsMalformedIndexAndRecommendsRepair()
    {
        using var temporary = new TemporaryDirectory();
        var appRoot = temporary.Combine("FiveM.app");
        var cacheDirectory = Path.Combine(appRoot, "data", "server-cache");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(cacheDirectory, "content_index.xml"), "<not-well-formed");

        var action = new CacheIndexIntegrityDiagnosisAction(appRoot);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains(result.Messages, message =>
            message.Contains("corrompido", StringComparison.Ordinal)
            && message.Contains("reparo de cache", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CacheIndexIntegrityDiagnosisAction_AcceptsWellFormedIndex()
    {
        using var temporary = new TemporaryDirectory();
        var appRoot = temporary.Combine("FiveM.app");
        var cacheDirectory = Path.Combine(appRoot, "data", "server-cache");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(cacheDirectory, "content_index.xml"), "<index><entry/></index>");

        var action = new CacheIndexIntegrityDiagnosisAction(appRoot);

        var result = await action.ApplyAsync(Context(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Contains(result.Messages, message => message.Contains("bem formado", StringComparison.Ordinal));
    }

    [Fact]
    public void GpuVendorDetection_ClassifiesKnownVendorsAndNeverWritesAnything()
    {
        var snapshot = new GpuVendorSnapshot(["NVIDIA GeForce RTX 4070", "AMD Radeon RX 7800 XT"]);

        var message = GpuVendorDetectionAction.Classify(snapshot);

        Assert.Contains("NVIDIA", message, StringComparison.Ordinal);
        Assert.Contains("AMD", message, StringComparison.Ordinal);
        Assert.Contains("painel oficial", message, StringComparison.Ordinal);
        Assert.Contains("não escreve nem sobrescreve", message, StringComparison.Ordinal);
    }

    [Fact]
    public void GpuVendorDetection_ReportsWhenNothingWasFound()
    {
        var message = GpuVendorDetectionAction.Classify(new GpuVendorSnapshot([]));

        Assert.Contains("Não foi possível identificar", message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewDiagnosticActions_AreAlwaysOnReadOnlyAndNonCritical()
    {
        string[] ids =
        [
            OptimizationActionIds.DiagnoseBottleneck,
            OptimizationActionIds.DetectOverlaysAndCaptureSoftware,
            OptimizationActionIds.ReadFiveMLegacyLogs,
            OptimizationActionIds.GuidePerformanceDiagnostics,
            OptimizationActionIds.DiagnoseNetworkHealth,
            OptimizationActionIds.DiagnoseThermalThrottling,
            OptimizationActionIds.DiagnosePagefileCommit,
            OptimizationActionIds.DiagnoseCacheIntegrity,
            OptimizationActionIds.DetectGpuVendor
        ];

        foreach (var id in ids)
        {
            var definition = ActionCatalog.Current.GetRequired(id);
            Assert.Equal(FiveMCleaner.Contracts.ActionReversibility.ReadOnly, definition.Reversibility);
            Assert.Equal(FiveMCleaner.Contracts.ActionRisk.Informational, definition.Risk);
            Assert.False(definition.IsCritical);
            Assert.Equal(3, definition.SupportedProfiles.Count);
        }
    }

    private static WindowsActionContext Context()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }

    private sealed class ThrowingSystemResourceInspector : ISystemResourceInspector
    {
        public SystemResourceSnapshot GetSnapshot()
        {
            throw new InvalidOperationException("simulated failure reading system resources");
        }
    }
}
