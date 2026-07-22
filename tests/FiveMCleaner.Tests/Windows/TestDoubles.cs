using FiveMCleaner.Windows;
using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Engine;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Tests.Windows;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FiveMCleaner.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts)
    {
        return parts.Aggregate(Path, System.IO.Path.Combine);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class FakeRegistryStore : IRegistryStore
{
    private readonly Dictionary<RegistryAddress, RegistryValueState> values = [];

    public RegistryValueState Read(RegistryAddress address)
    {
        return values.TryGetValue(address, out var value)
            ? value
            : RegistryValueState.Missing;
    }

    public void Write(RegistryAddress address, RegistryValueState state)
    {
        values[address] = state;
    }

    public void Delete(RegistryAddress address)
    {
        values.Remove(address);
    }
}

internal sealed class FakeProcessInspector(bool running = false) : IFiveMProcessInspector
{
    public bool Running { get; set; } = running;

    public int CallCount { get; private set; }

    public string? LastInstallationRoot { get; private set; }

    public bool IsRunningFrom(string installationRoot)
    {
        CallCount++;
        LastInstallationRoot = installationRoot;
        return Running;
    }
}

internal sealed class FakeGtaVProcessInspector(bool running = false) : IGtaVProcessInspector
{
    public bool Running { get; set; } = running;

    public int CallCount { get; private set; }

    public string? LastInstallationRoot { get; private set; }

    public bool IsRunningFrom(string? installationRoot)
    {
        CallCount++;
        LastInstallationRoot = installationRoot;
        return Running;
    }
}

internal sealed class SequencedGtaVProcessInspector(params bool[] runningStates) : IGtaVProcessInspector
{
    private readonly Queue<bool> states = new(runningStates);

    public int CallCount { get; private set; }

    public bool IsRunningFrom(string? installationRoot)
    {
        CallCount++;
        return states.Count > 0 && states.Dequeue();
    }
}

internal sealed class FakeVisualEffectsController : IVisualEffectsController
{
    public VisualEffectsState State { get; set; } = new(true, true, true);

    public VisualEffectsState Get() => State;

    public void Set(VisualEffectsState state) => State = state;
}

internal sealed class FakePowerStatusProvider(bool isOnAcPower = true) : IPowerStatusProvider
{
    public bool OnAcPower { get; set; } = isOnAcPower;

    public bool IsOnAcPower() => OnAcPower;
}

internal sealed class FakePowerPlanController : IPowerPlanController
{
    public Guid ActiveScheme { get; set; } = Guid.NewGuid();

    public Guid PerformanceScheme { get; set; } = Guid.NewGuid();

    public bool PerformanceAvailable { get; set; } = true;

    public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ActiveScheme);
    }

    public Task<bool> TryActivatePerformanceSchemeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PerformanceAvailable)
        {
            ActiveScheme = PerformanceScheme;
        }

        return Task.FromResult(PerformanceAvailable);
    }

    public Task ActivateSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActiveScheme = schemeId;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryJournalStore : IWindowsTransactionJournalStore
{
    private readonly Dictionary<Guid, WindowsTransactionJournal> journals = [];

    public Task SaveAsync(WindowsTransactionJournal journal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        journals[journal.TransactionId] = journal;
        return Task.CompletedTask;
    }

    public Task<WindowsTransactionJournal?> LoadAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        journals.TryGetValue(transactionId, out var journal);
        return Task.FromResult(journal);
    }

    public WindowsTransactionJournal Get(Guid transactionId) => journals[transactionId];
}

internal static class WindowsTestRuntime
{
    public static (
        WindowsOptimizationRuntime Runtime,
        WindowsOptimizationEnvironment Environment,
        InMemoryJournalStore Journals) Create(TemporaryDirectory temporaryDirectory)
    {
        var installation = temporaryDirectory.Combine("FiveM");
        var gtaVInstallation = temporaryDirectory.Combine("Grand Theft Auto V");
        var environment = new WindowsOptimizationEnvironment
        {
            FiveMInstallationRoot = installation,
            FiveMAppRoot = System.IO.Path.Combine(installation, "FiveM.app"),
            FiveMExecutablePath = System.IO.Path.Combine(installation, "FiveM.exe"),
            LegacyGraphicsSettingsPath = temporaryDirectory.Combine(
                "Roaming",
                "CitizenFX",
                "gta5_settings.xml"),
            GtaVInstallationRoot = gtaVInstallation,
            GtaVExecutablePath = System.IO.Path.Combine(gtaVInstallation, "GTA5.exe"),
            GtaVGraphicsSettingsPath = temporaryDirectory.Combine(
                "Documents",
                "Rockstar Games",
                "GTA V",
                "settings.xml"),
            UserTemporaryDirectory = temporaryDirectory.Combine("UserTemp"),
            JournalDirectory = temporaryDirectory.Combine("Journals")
        };
        var journals = new InMemoryJournalStore();
        var dependencies = new WindowsOptimizationDependencies
        {
            Registry = new FakeRegistryStore(),
            ProcessInspector = new FakeProcessInspector(),
            GtaVProcessInspector = new FakeGtaVProcessInspector(),
            FileTree = new SafeFileTree(),
            VisualEffects = new FakeVisualEffectsController(),
            PowerPlans = new FakePowerPlanController(),
            PowerStatus = new FakePowerStatusProvider(),
            JournalStore = journals
        };

        return (
            WindowsOptimizationRuntime.Create(environment, dependencies),
            environment,
            journals);
    }
}
