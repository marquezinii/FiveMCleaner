using Ralven.Contracts;
using Ralven.Windows.Actions;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class PointerAccelerationActionTests
{
    [Fact]
    public async Task ApplyAndRollbackPreserveTheExactPreviousValues()
    {
        var previous = Available(6, 10, 2);
        var controller = new FakeController(previous);
        var action = new PointerAccelerationAction(controller, static (key, _) => key);

        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Changed);
        Assert.Equal(Available(0, 0, 0), controller.Snapshot);
        Assert.Equal("ActionResults.PointerResponse.Applied", Assert.Single(result.Messages));

        await action.RollbackAsync(Context(), result.SnapshotJson, TestContext.Current.CancellationToken);
        Assert.Equal(previous, controller.Snapshot);
    }

    [Fact]
    public async Task RollbackRefusesToOverwriteANewerUserChoice()
    {
        var controller = new FakeController(Available(6, 10, 1));
        var action = new PointerAccelerationAction(controller, static (key, _) => key);
        var result = await action.ApplyAsync(Context(), TestContext.Current.CancellationToken);
        controller.Snapshot = Available(4, 8, 1);

        await Assert.ThrowsAsync<IOException>(() =>
            action.RollbackAsync(Context(), result.SnapshotJson, TestContext.Current.CancellationToken));
        Assert.Equal(Available(4, 8, 1), controller.Snapshot);
    }

    private static MouseAccelerationSnapshot Available(int first, int second, int level) => new(
        MouseAccelerationInspectionState.Available,
        first,
        second,
        level);

    private static WindowsActionContext Context() => new()
    {
        TransactionId = Guid.NewGuid(),
        StartedAtUtc = DateTimeOffset.UtcNow,
        IsElevated = false,
        PersonalUsage = PersonalUsage.Gaming
    };

    private sealed class FakeController(MouseAccelerationSnapshot snapshot) : IMouseAccelerationController
    {
        public MouseAccelerationSnapshot Snapshot { get; set; } = snapshot;

        public MouseAccelerationSnapshot GetSnapshot() => Snapshot;

        public void Set(int threshold1, int threshold2, int accelerationLevel) => Snapshot = Available(
            threshold1,
            threshold2,
            accelerationLevel);
    }
}
