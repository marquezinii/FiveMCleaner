using System.ComponentModel;
using System.Runtime.InteropServices;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;

namespace FiveMCleaner.Windows.Actions;

public sealed record VisualEffectsState(
    bool UiEffects,
    bool ClientAreaAnimation,
    bool MinimizeAnimation);

internal sealed record VisualEffectsSnapshot(
    VisualEffectsState Previous,
    VisualEffectsState Applied);

public interface IVisualEffectsController
{
    VisualEffectsState Get();

    void Set(VisualEffectsState state);
}

public sealed class WindowsVisualEffectsController : IVisualEffectsController
{
    private const uint SpiGetAnimation = 0x0048;
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpiGetUiEffects = 0x103E;
    private const uint SpiSetUiEffects = 0x103F;
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint SpiSetClientAreaAnimation = 0x1043;
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
        if (!SystemParametersInfoBoolean(
            action,
            0,
            ref value,
            SpifUpdateIniFile | SpifSendChange))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

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
                "Os efeitos visuais já estavam no estado solicitado."));
        }

        try
        {
            controller.Set(desired);
        }
        catch
        {
            controller.Set(previous);
            throw;
        }

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new VisualEffectsSnapshot(previous, desired),
            "Efeitos visuais atualizados por API oficial do Windows."));
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
        return Task.CompletedTask;
    }
}
