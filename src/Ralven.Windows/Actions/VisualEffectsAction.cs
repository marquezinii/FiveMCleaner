using System.ComponentModel;
using System.Runtime.InteropServices;
using Ralven.Contracts;
using Ralven.Core.Catalog;

namespace Ralven.Windows.Actions;

public sealed record VisualEffectsState(
    bool UiEffects,
    bool ClientAreaAnimation,
    bool MinimizeAnimation);

internal sealed record VisualEffectsSnapshot(
    VisualEffectsState Previous,
    VisualEffectsState Applied);

internal sealed record MenuShowDelaySnapshot(
    int PreviousMilliseconds,
    int AppliedMilliseconds);

public interface IVisualEffectsController
{
    VisualEffectsState Get();

    void Set(VisualEffectsState state);

    int GetMenuShowDelay();

    void SetMenuShowDelay(int milliseconds);
}

public sealed class WindowsVisualEffectsController : IVisualEffectsController
{
    private const uint SpiGetAnimation = 0x0048;
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpiGetUiEffects = 0x103E;
    private const uint SpiSetUiEffects = 0x103F;
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const uint SpiGetMenuShowDelay = 0x006A;
    private const uint SpiSetMenuShowDelay = 0x006B;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;

    public VisualEffectsState Get()
    {
        var uiEffects = GetBoolean(SpiGetUiEffects);
        var clientAreaAnimation = GetBoolean(SpiGetClientAreaAnimation);
        var animation = new AnimationInfo
        {
            Size = (uint)Marshal.SizeOf<AnimationInfo>()
        };

        if (!SystemParametersInfoAnimation(SpiGetAnimation, animation.Size, ref animation, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new VisualEffectsState(
            uiEffects,
            clientAreaAnimation,
            animation.MinimizeAnimation != 0);
    }

    public void Set(VisualEffectsState state)
    {
        SetBoolean(SpiSetUiEffects, state.UiEffects);
        SetBoolean(SpiSetClientAreaAnimation, state.ClientAreaAnimation);

        var animation = new AnimationInfo
        {
            Size = (uint)Marshal.SizeOf<AnimationInfo>(),
            MinimizeAnimation = state.MinimizeAnimation ? 1 : 0
        };
        if (!SystemParametersInfoAnimation(
            SpiSetAnimation,
            animation.Size,
            ref animation,
            SpifUpdateIniFile | SpifSendChange))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public int GetMenuShowDelay()
    {
        uint milliseconds = 0;
        if (!SystemParametersInfoUnsignedInteger(SpiGetMenuShowDelay, 0, ref milliseconds, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return checked((int)milliseconds);
    }

    public void SetMenuShowDelay(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        if (!SystemParametersInfoPointer(
            SpiSetMenuShowDelay,
            checked((uint)milliseconds),
            IntPtr.Zero,
            SpifUpdateIniFile | SpifSendChange))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static bool GetBoolean(uint action)
    {
        var value = false;
        if (!SystemParametersInfoBoolean(action, 0, ref value, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return value;
    }

    private static void SetBoolean(uint action, bool value)
    {
        if (!SystemParametersInfoPointer(
            action,
            0,
            BooleanParameter(value),
            SpifUpdateIniFile | SpifSendChange))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static IntPtr BooleanParameter(bool value) => value ? new IntPtr(1) : IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;

        public int MinimizeAnimation;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoBoolean(
        uint action,
        uint parameter,
        [MarshalAs(UnmanagedType.Bool)] ref bool value,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoAnimation(
        uint action,
        uint parameter,
        ref AnimationInfo value,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoUnsignedInteger(
        uint action,
        uint parameter,
        ref uint value,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoPointer(
        uint action,
        uint parameter,
        IntPtr value,
        uint flags);
}

public sealed class VisualEffectsAction : WindowsOptimizationAction
{
    private readonly IVisualEffectsController controller;
    private readonly VisualEffectsState desired;

    public VisualEffectsAction(IVisualEffectsController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        desired = new VisualEffectsState(
            UiEffects: false,
            ClientAreaAnimation: false,
            MinimizeAnimation: false);
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ReduceWindowsVisualEffects);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = controller.Get();
        if (previous == desired)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                WindowsActionText.Format("ActionResults.VisualEffects.AlreadyDesired")));
        }

        try
        {
            controller.Set(desired);
            if (controller.Get() != desired)
            {
                throw new IOException("Windows did not apply the requested visual effects settings.");
            }
        }
        catch (Exception applyException)
        {
            try
            {
                controller.Set(previous);
                if (controller.Get() != previous)
                {
                    throw new IOException("Windows did not restore the previous visual effects settings after apply failed.");
                }
            }
            catch (Exception restoreException)
            {
                throw new AggregateException(
                    "Applying and restoring the Windows visual effects settings both failed.",
                    applyException,
                    restoreException);
            }

            throw;
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new VisualEffectsSnapshot(previous, desired),
            WindowsActionText.Format("ActionResults.VisualEffects.Applied")));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<VisualEffectsSnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        if (controller.Get() != snapshot.Applied)
        {
            throw new IOException(
                "Visual effects changed after optimization; rollback refused to overwrite newer settings.");
        }

        controller.Set(snapshot.Previous);
        if (controller.Get() != snapshot.Previous)
        {
            throw new IOException("Windows did not restore the previous visual effects settings.");
        }

        return Task.CompletedTask;
    }
}

public sealed class MenuShowDelayAction : WindowsOptimizationAction
{
    private const int MaximumDelayMilliseconds = 100;
    private readonly IVisualEffectsController controller;
    private readonly WindowsActionTextResolver text;

    public MenuShowDelayAction(
        IVisualEffectsController controller,
        WindowsActionTextResolver text)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ReduceMenuShowDelay);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = controller.GetMenuShowDelay();
        var applied = Math.Min(previous, MaximumDelayMilliseconds);
        if (previous == applied)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                text("ActionResults.MenuShowDelay.AlreadyOptimized")));
        }

        try
        {
            controller.SetMenuShowDelay(applied);
            if (controller.GetMenuShowDelay() != applied)
            {
                throw new IOException("Windows did not apply the requested menu show delay.");
            }
        }
        catch (Exception applyException)
        {
            try
            {
                controller.SetMenuShowDelay(previous);
                if (controller.GetMenuShowDelay() != previous)
                {
                    throw new IOException("Windows did not restore the previous menu show delay after apply failed.");
                }
            }
            catch (Exception restoreException)
            {
                throw new AggregateException(
                    "Applying and restoring the Windows menu show delay both failed.",
                    applyException,
                    restoreException);
            }

            throw;
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new MenuShowDelaySnapshot(previous, applied),
            text("ActionResults.MenuShowDelay.Applied")));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<MenuShowDelaySnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.AppliedMilliseconds != MaximumDelayMilliseconds
            || snapshot.PreviousMilliseconds <= MaximumDelayMilliseconds)
        {
            throw new InvalidDataException("The menu show delay snapshot is outside this action's supported values.");
        }

        if (controller.GetMenuShowDelay() != snapshot.AppliedMilliseconds)
        {
            throw new IOException(
                "Menu show delay changed after optimization; rollback refused to overwrite newer settings.");
        }

        controller.SetMenuShowDelay(snapshot.PreviousMilliseconds);
        if (controller.GetMenuShowDelay() != snapshot.PreviousMilliseconds)
        {
            throw new IOException("Windows did not restore the previous menu show delay.");
        }

        return Task.CompletedTask;
    }
}
