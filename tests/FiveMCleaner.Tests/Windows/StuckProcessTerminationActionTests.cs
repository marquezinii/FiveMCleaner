using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class StuckProcessTerminationActionTests
{
    private static readonly string InstallationRoot = Path.GetFullPath(@"C:\Games\FiveM");

    [Fact]
    public async Task Apply_WhenNoStuckProcessExists_DoesNotInvokeTerminator()
    {
        var terminator = new FakeFiveMProcessTerminator();
        var action = new StuckProcessTerminationAction(
            InstallationRoot,
            new FakeStuckFiveMProcessInspector(),
            terminator);

        var result = await action.ApplyAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(0, terminator.CallCount);
    }

    [Fact]
    public async Task Apply_RevalidatesTheInspectedProcessThroughTheTerminator()
    {
        var snapshot = new StuckFiveMProcessSnapshot(true, 42, "FiveM_b3095_GTAProcess");
        var inspector = new FakeStuckFiveMProcessInspector { Snapshot = snapshot };
        var terminator = new FakeFiveMProcessTerminator();
        var action = new StuckProcessTerminationAction(InstallationRoot, inspector, terminator);

        var result = await action.ApplyAsync(CreateContext(), CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Equal(1, terminator.CallCount);
        Assert.Equal(snapshot, terminator.LastSnapshot);
        Assert.Equal(InstallationRoot, terminator.LastInstallationRoot);
    }

    [Fact]
    public async Task Apply_WhenRevalidationFails_ReportsNoChange()
    {
        var inspector = new FakeStuckFiveMProcessInspector
        {
            Snapshot = new StuckFiveMProcessSnapshot(true, 42, "FiveM_b3095_GTAProcess")
        };
        var terminator = new FakeFiveMProcessTerminator { TerminateSucceeds = false };
        var action = new StuckProcessTerminationAction(InstallationRoot, inspector, terminator);

        var result = await action.ApplyAsync(CreateContext(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(1, terminator.CallCount);
    }

    private static WindowsActionContext CreateContext()
    {
        return new WindowsActionContext
        {
            TransactionId = Guid.NewGuid(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            IsElevated = false
        };
    }
}
