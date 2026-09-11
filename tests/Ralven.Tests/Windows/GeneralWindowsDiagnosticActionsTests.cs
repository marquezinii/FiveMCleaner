using System.Globalization;
using Ralven.App.Services;
using Ralven.Windows.Actions;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class GeneralWindowsDiagnosticActionsTests
{
    private static readonly ILocalizationService Portuguese = new LocalizationService(
        CultureInfo.GetCultureInfo("pt-BR"));

    [Fact]
    public async Task SecurityHealth_ReportsPartialStateWithoutExposingHResults()
    {
        var snapshot = new WindowsSystemHealthSnapshot(
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Poor, -1),
            new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Unavailable, -2),
            DateTimeOffset.UtcNow);
        var action = new WindowsSecurityHealthDiagnosisAction(
            new SystemHealthInspectorStub(snapshot),
            Portuguese.Format);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        Assert.False(result.Changed);
        Assert.Null(result.SnapshotJson);
        var message = Assert.Single(result.Messages);
        Assert.Contains("Leitura parcial", message, StringComparison.Ordinal);
        Assert.Contains("firewall: requer atenção", message, StringComparison.Ordinal);
        Assert.DoesNotContain("-1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("-2", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecurityHealth_ReportsUnavailableWhenInspectorCannotRead()
    {
        var action = new WindowsSecurityHealthDiagnosisAction(
            new SystemHealthInspectorStub(new IOException("sensitive detail")),
            Portuguese.Format);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        var message = Assert.Single(result.Messages);
        Assert.Contains("Não foi possível", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive detail", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupLoad_ReportsAggregateCountsWithoutNamesOrLocations()
    {
        var snapshot = Inventory(
            startupItems:
            [
                new WindowsStartupItem("Private app", @"C:\Users\Private\app.exe", WindowsStartupItemSource.RegistryRun, WindowsApplicationScope.CurrentUser),
                new WindowsStartupItem("Machine agent", "LocalMachine:StartupFolder", WindowsStartupItemSource.StartupFolder, WindowsApplicationScope.LocalMachine)
            ],
            startupItemsComplete: true);
        var action = new StartupLoadDiagnosisAction(
            new ApplicationInventoryInspectorStub(snapshot),
            Portuguese.Format);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        Assert.False(result.Changed);
        Assert.Null(result.SnapshotJson);
        var message = Assert.Single(result.Messages);
        Assert.Contains("Itens de inicialização detectados: 2", message, StringComparison.Ordinal);
        Assert.Contains("registro: 1; pastas: 1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Private app", message, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\Private", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupLoad_PreservesPartialSemanticsWhenNoItemsAreReadable()
    {
        var action = new StartupLoadDiagnosisAction(
            new ApplicationInventoryInspectorStub(Inventory([], startupItemsComplete: false)),
            Portuguese.Format);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        var message = Assert.Single(result.Messages);
        Assert.Contains("fontes acessíveis", message, StringComparison.Ordinal);
        Assert.Contains("Leitura parcial", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_PropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var security = new WindowsSecurityHealthDiagnosisAction(
            new SystemHealthInspectorStub(new WindowsSystemHealthSnapshot(
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                DateTimeOffset.UtcNow)),
            Portuguese.Format);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => security.ApplyAsync(Context(), cancellation.Token));
    }

    [Fact]
    public async Task TrimStatus_ReportsPolicyWithoutClaimingHardwareSupport()
    {
        var action = new TrimStatusDiagnosisAction(new TrimStatusInspectorStub(
            new TrimStatusSnapshot(
                TrimInspectionState.Available,
                TrimDeleteNotificationState.Enabled,
                TrimDeleteNotificationState.Disabled)),
            Portuguese.Format);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        var message = Assert.Single(result.Messages);
        Assert.Contains("NTFS: notificações habilitadas", message, StringComparison.Ordinal);
        Assert.Contains("ReFS: notificações desabilitadas", message, StringComparison.Ordinal);
        Assert.Contains("não confirma suporte do dispositivo", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrimStatus_PreservesAccessDeniedAsUnavailableWithoutMutation()
    {
        var action = new TrimStatusDiagnosisAction(new TrimStatusInspectorStub(
            new TrimStatusSnapshot(TrimInspectionState.AccessDenied, null, null)),
            Portuguese.Format);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        Assert.False(result.Changed);
        Assert.Contains("sem elevação", Assert.Single(result.Messages), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "desativada")]
    [InlineData(1, "ativa")]
    [InlineData(2, "ativa")]
    public async Task MouseAcceleration_ReportsDocumentedLevelWithoutChangingIt(
        int level,
        string expected)
    {
        var action = new MouseAccelerationDiagnosisAction(new MouseAccelerationInspectorStub(
            new MouseAccelerationSnapshot(
                MouseAccelerationInspectionState.Available,
                6,
                10,
                level)),
            Portuguese.Format);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        Assert.False(result.Changed);
        Assert.Contains(expected, Assert.Single(result.Messages), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "healthy state", "Startup items detected", "TRIM policy", "pointer acceleration")]
    [InlineData("pt-BR", "estado saudável", "Itens de inicialização detectados", "Política de TRIM", "aceleração do ponteiro")]
    [InlineData("es", "estado saludable", "Elementos de inicio detectados", "Política de TRIM", "aceleración del puntero")]
    public async Task Diagnostics_UseTheSelectedLanguage(
        string cultureName,
        string securityText,
        string startupText,
        string trimText,
        string mouseText)
    {
        var localization = new LocalizationService(CultureInfo.GetCultureInfo(cultureName));
        var security = new WindowsSecurityHealthDiagnosisAction(
            new SystemHealthInspectorStub(new WindowsSystemHealthSnapshot(
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                new WindowsSecurityProviderHealth(WindowsSecurityHealthState.Good, 0),
                DateTimeOffset.UtcNow)),
            localization.Format);
        var startup = new StartupLoadDiagnosisAction(
            new ApplicationInventoryInspectorStub(Inventory(
                [new WindowsStartupItem("Private", "Private", WindowsStartupItemSource.RegistryRun, WindowsApplicationScope.CurrentUser)],
                startupItemsComplete: true)),
            localization.Format);
        var trim = new TrimStatusDiagnosisAction(
            new TrimStatusInspectorStub(new TrimStatusSnapshot(
                TrimInspectionState.Available,
                TrimDeleteNotificationState.Enabled,
                TrimDeleteNotificationState.Enabled)),
            localization.Format);
        var mouse = new MouseAccelerationDiagnosisAction(
            new MouseAccelerationInspectorStub(new MouseAccelerationSnapshot(
                MouseAccelerationInspectionState.Available,
                6,
                10,
                1)),
            localization.Format);

        var cancellationToken = TestContext.Current.CancellationToken;
        var messages = new[]
        {
            Assert.Single((await security.ApplyAsync(Context(), cancellationToken)).Messages),
            Assert.Single((await startup.ApplyAsync(Context(), cancellationToken)).Messages),
            Assert.Single((await trim.ApplyAsync(Context(), cancellationToken)).Messages),
            Assert.Single((await mouse.ApplyAsync(Context(), cancellationToken)).Messages)
        };

        Assert.Contains(securityText, messages[0], StringComparison.Ordinal);
        Assert.Contains(startupText, messages[1], StringComparison.Ordinal);
        Assert.Contains(trimText, messages[2], StringComparison.Ordinal);
        Assert.Contains(mouseText, messages[3], StringComparison.Ordinal);
    }

    private static WindowsActionContext Context() => new()
    {
        TransactionId = Guid.NewGuid(),
        StartedAtUtc = DateTimeOffset.UtcNow,
        IsElevated = false
    };

    private static WindowsApplicationInventorySnapshot Inventory(
        IReadOnlyList<WindowsStartupItem> startupItems,
        bool startupItemsComplete) => new(
            [],
            startupItems,
            DateTimeOffset.UtcNow,
            InstalledApplicationsComplete: true,
            StartupItemsComplete: startupItemsComplete);

    private sealed class SystemHealthInspectorStub : IWindowsSystemHealthInspector
    {
        private readonly WindowsSystemHealthSnapshot? snapshot;
        private readonly Exception? exception;

        public SystemHealthInspectorStub(WindowsSystemHealthSnapshot snapshot) => this.snapshot = snapshot;

        public SystemHealthInspectorStub(Exception exception) => this.exception = exception;

        public Task<WindowsSystemHealthSnapshot> InspectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return exception is null
                ? Task.FromResult(snapshot!)
                : Task.FromException<WindowsSystemHealthSnapshot>(exception);
        }
    }

    private sealed class ApplicationInventoryInspectorStub : IWindowsApplicationInventoryInspector
    {
        private readonly WindowsApplicationInventorySnapshot snapshot;

        public ApplicationInventoryInspectorStub(WindowsApplicationInventorySnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public Task<WindowsApplicationInventorySnapshot> InspectAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The startup diagnosis must not request the full inventory.");

        public Task<WindowsApplicationInventorySnapshot> InspectStartupAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }

    private sealed class TrimStatusInspectorStub : ITrimStatusInspector
    {
        private readonly TrimStatusSnapshot snapshot;

        public TrimStatusInspectorStub(TrimStatusSnapshot snapshot) => this.snapshot = snapshot;

        public Task<TrimStatusSnapshot> InspectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }

    private sealed class MouseAccelerationInspectorStub : IMouseAccelerationInspector
    {
        private readonly MouseAccelerationSnapshot snapshot;

        public MouseAccelerationInspectorStub(MouseAccelerationSnapshot snapshot) => this.snapshot = snapshot;

        public MouseAccelerationSnapshot GetSnapshot() => snapshot;
    }
}
