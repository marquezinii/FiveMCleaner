using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Infrastructure;

namespace Ralven.Windows.Actions;

internal sealed record PointerAccelerationSnapshot(
    MouseAccelerationSnapshot Previous,
    MouseAccelerationSnapshot Applied);

public sealed class PointerAccelerationAction : WindowsOptimizationAction
{
    private static readonly MouseAccelerationSnapshot Desired = new(
        MouseAccelerationInspectionState.Available,
        0,
        0,
        0);

    private readonly IMouseAccelerationController controller;
    private readonly WindowsActionTextResolver text;

    public PointerAccelerationAction(
        IMouseAccelerationController controller,
        WindowsActionTextResolver text)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DisableMouseAcceleration);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = controller.GetSnapshot();
        if (!IsValid(previous))
        {
            return Task.FromResult(WindowsActionApplyResult.Skipped(
                text("ActionResults.PointerResponse.Unavailable")));
        }

        if (previous == Desired)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                text("ActionResults.PointerResponse.AlreadyConsistent")));
        }

        try
        {
            Set(Desired);
            if (controller.GetSnapshot() != Desired)
            {
                throw new IOException("Windows did not apply the requested pointer acceleration settings.");
            }
        }
        catch (Exception applyException)
        {
            try
            {
                Set(previous);
                if (controller.GetSnapshot() != previous)
                {
                    throw new IOException("Windows did not restore pointer acceleration after apply failed.");
                }
            }
            catch (Exception restoreException)
            {
                throw new AggregateException(
                    "Applying and restoring pointer acceleration both failed.",
                    applyException,
                    restoreException);
            }

            throw;
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new PointerAccelerationSnapshot(previous, Desired),
            text("ActionResults.PointerResponse.Applied")));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<PointerAccelerationSnapshot>(snapshotJson);
        if (!IsValid(snapshot.Previous) || snapshot.Applied != Desired)
        {
            throw new InvalidDataException("Pointer acceleration snapshot is outside the supported values.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (controller.GetSnapshot() != Desired)
        {
            throw new IOException(
                "Pointer acceleration changed after optimization; rollback refused to overwrite newer settings.");
        }

        Set(snapshot.Previous);
        if (controller.GetSnapshot() != snapshot.Previous)
        {
            throw new IOException("Windows did not restore the previous pointer acceleration settings.");
        }

        return Task.CompletedTask;
    }

    private void Set(MouseAccelerationSnapshot state) => controller.Set(
        state.Threshold1!.Value,
        state.Threshold2!.Value,
        state.AccelerationLevel!.Value);

    private static bool IsValid(MouseAccelerationSnapshot state) =>
        state.State == MouseAccelerationInspectionState.Available
        && state.Threshold1 is >= 0
        && state.Threshold2 is >= 0
        && state.AccelerationLevel is >= 0 and <= 2;
}
