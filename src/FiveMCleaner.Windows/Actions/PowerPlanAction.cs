using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

internal sealed record PowerPlanSnapshot(Guid PreviousScheme, Guid AppliedScheme);

public interface IPowerStatusProvider
{
    bool IsOnAcPower();
}

public sealed class WindowsPowerStatusProvider : IPowerStatusProvider
{
    public bool IsOnAcPower()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return status.AcLineStatus == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}

public interface IPowerPlanController
{
    Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken);

    Task<bool> TryActivatePerformanceSchemeAsync(CancellationToken cancellationToken);

    Task ActivateSchemeAsync(Guid schemeId, CancellationToken cancellationToken);
}

public sealed partial class PowerCfgController : IPowerPlanController
{
    private readonly ICommandRunner commandRunner;
    private readonly string powerCfgPath;

    public PowerCfgController(ICommandRunner commandRunner)
    {
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        powerCfgPath = Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "powercfg.exe"));
        if (!File.Exists(powerCfgPath))
        {
            throw new FileNotFoundException("The Windows powercfg executable was not found.", powerCfgPath);
        }
    }

    public async Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            powerCfgPath,
            ["/GETACTIVESCHEME"],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"powercfg failed while reading the active scheme (exit {result.ExitCode}).");
        }

        var match = PowerSchemeGuidRegex().Match(result.StandardOutput);
        return match.Success && Guid.TryParse(match.Value, out var scheme)
            ? scheme
            : throw new InvalidOperationException("powercfg did not return a valid active scheme GUID.");
    }

    public async Task<bool> TryActivatePerformanceSchemeAsync(
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            powerCfgPath,
            ["/SETACTIVE", "SCHEME_MIN"],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task ActivateSchemeAsync(
        Guid schemeId,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            powerCfgPath,
            ["/SETACTIVE", schemeId.ToString("D")],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"powercfg failed while restoring scheme {schemeId:D} (exit {result.ExitCode}).");
        }
    }

    [GeneratedRegex(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant)]
    private static partial Regex PowerSchemeGuidRegex();
}

public sealed class SessionPerformancePowerPlanAction : WindowsOptimizationAction
{
    private readonly IPowerPlanController controller;
    private readonly IPowerStatusProvider powerStatus;

    public SessionPerformancePowerPlanAction(
        IPowerPlanController controller,
        IPowerStatusProvider powerStatus)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.powerStatus = powerStatus ?? throw new ArgumentNullException(nameof(powerStatus));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.EnableSessionPerformancePowerPlan);

    public override async Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.IsElevated)
        {
            throw new UnauthorizedAccessException("O modo de energia da sessão requer elevação.");
        }

        if (!powerStatus.IsOnAcPower())
        {
            return WindowsActionApplyResult.NoChange(
                "O modo de alto desempenho não foi ativado porque o computador está na bateria.");
        }

        var previous = await controller.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (!await controller.TryActivatePerformanceSchemeAsync(cancellationToken).ConfigureAwait(false))
        {
            return WindowsActionApplyResult.NoChange(
                "Este computador não expõe um plano de alto desempenho compatível.");
        }

        Guid applied;
        try
        {
            applied = await controller.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await controller.ActivateSchemeAsync(previous, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        if (applied == previous)
        {
            return WindowsActionApplyResult.NoChange(
                "O plano de alto desempenho já estava ativo.");
        }

        return WindowsActionApplyResult.ChangedWith(
            new PowerPlanSnapshot(previous, applied),
            "Plano de alto desempenho ativado; o estado anterior foi salvo para rollback.");
    }

    public override async Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        if (!context.IsElevated)
        {
            throw new UnauthorizedAccessException("Restaurar o plano de energia requer elevação.");
        }

        var snapshot = WindowsActionSnapshot.Deserialize<PowerPlanSnapshot>(snapshotJson);
        var current = await controller.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (current != snapshot.AppliedScheme)
        {
            throw new IOException(
                "O plano de energia mudou depois da otimização; o rollback preservou a escolha mais recente.");
        }

        await controller.ActivateSchemeAsync(snapshot.PreviousScheme, cancellationToken)
            .ConfigureAwait(false);
    }
}
