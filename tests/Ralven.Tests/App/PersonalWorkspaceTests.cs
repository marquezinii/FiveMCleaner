using Ralven.App.Services;
using Ralven.App.ViewModels;
using Ralven.Contracts;
using Ralven.Core.Planning;
using Xunit;

namespace Ralven.Tests.App;

public sealed class PersonalWorkspaceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PcObservation Observation => new(DateTimeOffset.UtcNow, new string('a', 64),
        "Windows 11", 40, WindowsGamingSettingState.Enabled, WindowsGamingSettingState.Disabled);

    [Fact]
    public async Task SavedRoutinesSurviveRestartAndExpiryOnlyBlocksNewPaidWork()
    {
        using var directory = new TemporaryDirectory();
        var authorized = true;
        var service = new PersonalWorkspaceService(_ => Task.FromResult(authorized), directory: directory.Path);
        foreach (var usage in Enum.GetValues<PersonalUsage>())
            await service.SaveProfileAsync(new() { Usage = usage }, Token);
        await service.SaveProfileAsync(new() { Usage = PersonalUsage.Work, AllowPerformancePower = true }, Token);
        await service.SetTrackingAsync(true, Observation, Token);

        authorized = false;
        await Assert.ThrowsAsync<ProAccessRequiredException>(() => service.SaveProfileAsync(new(), Token));
        await Assert.ThrowsAsync<ProAccessRequiredException>(() => service.ObserveAsync(Observation, Token));
        var stopped = await service.SetTrackingAsync(false, null, Token);
        Assert.False(stopped.TrackingEnabled);
        Assert.Equal(4, stopped.Profiles.Count);
        Assert.True(stopped.Profiles.Single(profile => profile.Usage == PersonalUsage.Work).AllowPerformancePower);

        var restarted = new PersonalWorkspaceService(_ => Task.FromResult(false), directory: directory.Path);
        var loaded = await restarted.LoadAsync(Token);
        Assert.Equal(stopped.Profiles, loaded.Profiles);
        Assert.Equal(stopped.Reference, loaded.Reference);
        Assert.False(loaded.TrackingEnabled);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"profiles\":null}")]
    [InlineData("{\"trackingEnabled\":true}")]
    public async Task CorruptOrUnsupportedDataIsPreserved(string content)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Combine("workspace.json");
        await File.WriteAllTextAsync(path, content, Token);
        var service = new PersonalWorkspaceService(_ => Task.FromResult(true), directory: directory.Path);

        await Assert.ThrowsAnyAsync<Exception>(() => service.LoadAsync(Token));
        await Assert.ThrowsAnyAsync<Exception>(() => service.SaveProfileAsync(new(), Token));
        Assert.Equal(content, await File.ReadAllTextAsync(path, Token));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task OversizedStorageAndCancelledWritesDoNotDestroyExistingData()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Combine("workspace.json");
        var service = new PersonalWorkspaceService(_ => Task.FromResult(true), directory: directory.Path);
        await service.SaveProfileAsync(new(), Token);
        var original = await File.ReadAllTextAsync(path, Token);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveProfileAsync(new(), cancellation.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(path, Token));
        await File.WriteAllTextAsync(path, new string(' ', 512 * 1024 + 1), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(Token));
        Assert.Equal(512 * 1024 + 1, new FileInfo(path).Length);
    }

    [Fact]
    public async Task TrackingIsOptInAndKeepsOnlyTheLatestSixtyChanges()
    {
        var service = new PersonalWorkspaceService(_ => Task.FromResult(true), inMemory: true);
        var reference = Observation;
        Assert.Empty((await service.ObserveAsync(reference, Token)).Changes);
        await service.SetTrackingAsync(true, reference, Token);
        for (var i = 0; i < 70; i++)
            await service.ObserveAsync(reference with
            {
                CapturedAt = reference.CapturedAt.AddMinutes(i + 1),
                GameMode = i % 2 == 0 ? WindowsGamingSettingState.Disabled : WindowsGamingSettingState.Enabled
            }, Token);
        var workspace = await service.LoadAsync(Token);
        Assert.Equal(60, workspace.Changes.Count);
        Assert.Equal(reference, workspace.Reference);
        Assert.All(workspace.Changes, change => Assert.Equal(PcChangeKind.GameMode, change.Kind));
        Assert.Equal(reference.CapturedAt.AddMinutes(70), workspace.LastObservation!.CapturedAt);
    }

    [Fact]
    public void UnavailableSensorsAreNotReportedAsConfigurationChanges()
    {
        var reference = Observation;
        var changes = PersonalWorkspaceService.DetectChanges(reference, reference with
        {
            GameMode = WindowsGamingSettingState.Unknown,
            BackgroundCapture = WindowsGamingSettingState.Unavailable,
            FreeDiskGiB = 8
        });
        Assert.Equal([PcChangeKind.LowDiskSpace], changes.Select(change => change.Kind));
        Assert.Empty(PersonalWorkspaceService.DetectChanges(reference, reference));
    }

    [Fact]
    public void TrackingReportsPointerAccelerationDriftOnlyWhenBothReadingsAreAvailable()
    {
        var disabled = Observation with { PointerAccelerationEnabled = false };
        var enabled = Observation with { PointerAccelerationEnabled = true };

        Assert.Equal(
            [PcChangeKind.PointerAcceleration],
            PersonalWorkspaceService.DetectChanges(disabled, enabled).Select(change => change.Kind));
        Assert.Empty(PersonalWorkspaceService.DetectChanges(disabled, enabled with { PointerAccelerationEnabled = null }));
    }

    [Fact]
    public void MeasurementsRejectMissingCoverageAndIncompatibleComparisons()
    {
        var snapshots = Enumerable.Range(0, 30).Select(index => new LiveSystemMetricsSnapshot(
            25, index < 24 ? 50 : null, index < 23 ? 70 : null, double.NaN, 0, DateTimeOffset.UtcNow)).ToArray();
        var first = PersonalWorkspaceService.Summarize(PersonalUsage.Gaming, "Same scene", Observation, snapshots, 30);
        Assert.Equal(25d, first.CpuPercent);
        Assert.Equal(50d, first.GpuPercent);
        Assert.Null(first.MemoryPercent);
        Assert.Null(first.DiskPercent);
        Assert.True(PersonalWorkspaceService.CanCompare(first, first with { Context = "same scene", CpuPercent = 90 }));
        Assert.False(PersonalWorkspaceService.CanCompare(first, first with { Usage = PersonalUsage.Work }));
        Assert.False(PersonalWorkspaceService.CanCompare(first, first with { Context = "Other scene" }));
        Assert.False(PersonalWorkspaceService.CanCompare(first, first with { HardwareSignature = new string('b', 64) }));
        Assert.False(PersonalWorkspaceService.CanCompare(first, first with { WindowsVersion = "Other Windows" }));
        Assert.False(PersonalWorkspaceService.CanCompare(first, first with { DurationSeconds = 90 }));
        Assert.False(PersonalWorkspaceService.CanCompare(first, first with { SampleCount = 10 }));
        Assert.False(PersonalWorkspaceService.CanCompare(first, first with { CpuPercent = null, GpuPercent = null }));
    }

    [Fact]
    public async Task InvalidMeasurementContextAndCancellationNeverStoreARecord()
    {
        var service = new PersonalWorkspaceService(_ => Task.FromResult(true), inMemory: true);
        var progress = new Progress<int>();
        await Assert.ThrowsAsync<ArgumentException>(() => service.MeasureAsync(PersonalUsage.Work, " ", Observation, progress, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MeasureAsync(PersonalUsage.Work, new string('x', 81), Observation, progress, Token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.MeasureAsync(PersonalUsage.Work, "Editor", Observation, progress, cancellation.Token));
        Assert.Empty((await service.LoadAsync(Token)).Measurements);
    }

    [Fact]
    public async Task UiAccessCannotBypassServiceAuthorizationAndFreeModesStayAvailable()
    {
        var store = new PersonalWorkspaceService(_ => Task.FromResult(false), inMemory: true);
        using var viewModel = new MainViewModel(new FakeAppOptimizationService(new AppSettings(), false),
            personalWorkspaceService: store);
        await viewModel.InitializeAsync();
        foreach (var profile in Enum.GetValues<OptimizationProfile>())
        {
            viewModel.SelectProfile(profile);
            Assert.True(viewModel.CanStart);
        }

        viewModel.SelectUltra();
        Assert.True(viewModel.IsUltraSelected);
        Assert.False(viewModel.IsAggressiveSelected);
        Assert.False(viewModel.CanStart);
        viewModel.SetProAccess(true);
        Assert.True(viewModel.CanStart);
        await viewModel.SavePersonalProfileAsync();
        Assert.False(viewModel.HasProAccess);
        Assert.Empty((await store.LoadAsync(Token)).Profiles);
        viewModel.SelectProfile(OptimizationProfile.Aggressive);
        Assert.False(viewModel.IsUltraSelected);
        Assert.True(viewModel.CanStart);
        viewModel.SetOptimizationScope(OptimizationScope.FiveMLegacy);
        viewModel.SelectUltra();
        Assert.False(viewModel.IsUltraSelected);
    }

    [Fact]
    public async Task SavedPreferencesRestorePerRoutine()
    {
        var store = new PersonalWorkspaceService(_ => Task.FromResult(true), inMemory: true);
        await store.SaveProfileAsync(new() { Usage = PersonalUsage.Streaming, PreserveAppearance = false }, Token);
        using var viewModel = new MainViewModel(new FakeAppOptimizationService(new AppSettings(), false),
            personalWorkspaceService: store);
        await viewModel.InitializeAsync();
        viewModel.SetProAccess(true);
        viewModel.SelectUltra();
        viewModel.PersonalUsageIndex = (int)PersonalUsage.Streaming;
        Assert.False(viewModel.PersonalPreserveAppearance);
        Assert.True(viewModel.PersonalPreserveCapture);
        viewModel.PersonalUsageIndex = (int)PersonalUsage.Work;
        Assert.True(viewModel.PersonalPreserveAppearance);
    }

    [Fact]
    public async Task ExecutionRevalidatesProBeforeTheRuntimeIsEntered()
    {
        var calls = 0;
        var service = new AppOptimizationService(demoMode: true)
        {
            AuthorizePro = _ => { calls++; return Task.FromResult(false); }
        };
        var plan = PlanBuilder.Build(new OptimizationPlanRequestDto
        {
            Scope = OptimizationScope.GeneralWindows,
            Profile = OptimizationProfile.Aggressive,
            Edition = FiveMEdition.Unknown,
            PersonalPreferences = new()
        }, PlanBuildContext.New(TimeProvider.System));
        await Assert.ThrowsAsync<ProAccessRequiredException>(() => service.ExecuteAsync(plan, new Progress<AppProgressUpdate>(), Token));
        Assert.Equal(1, calls);
    }
}
