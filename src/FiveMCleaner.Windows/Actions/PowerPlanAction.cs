using System.ComponentModel;
using System.Globalization;
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

    /// <summary>
    /// True when Windows Battery Saver is currently active -- read from the
    /// same <c>GetSystemPowerStatus</c> call as <see cref="IsOnAcPower"/>,
    /// documented by Microsoft as bit 0 of <c>SystemStatusFlag</c>.
    /// </summary>
    bool IsBatterySaverActive();
}

public sealed class WindowsPowerStatusProvider : IPowerStatusProvider
{
    public bool IsOnAcPower() => GetStatus().AcLineStatus == 1;

    public bool IsBatterySaverActive() => (GetStatus().SystemStatusFlag & 1) == 1;

    private static SystemPowerStatus GetStatus()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return status;
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

/// <summary>
/// Result of attempting to switch to the high-performance power scheme.
/// Distinguishes "Windows genuinely refused because of a permission/policy
/// restriction" from "this computer simply doesn't expose that scheme" --
/// only the former should ever prompt for elevation.
/// </summary>
public enum PowerPlanActivationOutcome
{
    Activated,
    AccessDenied,
    SchemeUnavailable
}

public interface IPowerPlanController
{
    Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken);

    Task<PowerPlanActivationOutcome> TryActivatePerformanceSchemeAsync(CancellationToken cancellationToken);

    Task ActivateSchemeAsync(Guid schemeId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the PCI Express Link State Power Management policy of the
    /// active scheme (0 = Off, 1 = Moderate, 2 = Maximum power savings).
    /// Null when the setting is not exposed on this machine (some
    /// motherboards/chipsets do not surface it).
    /// </summary>
    Task<int?> GetPciExpressAspmPolicyAsync(CancellationToken cancellationToken);

    Task<bool> TrySetPciExpressAspmPolicyAsync(int policyValue, CancellationToken cancellationToken);
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

    // ERROR_ACCESS_DENIED. powercfg.exe surfaces this both as this exit code
    // and (locale-dependently) in stderr text, so the exit code is checked
    // first as the reliable signal and the text patterns are a fallback for
    // older/odd builds that don't set it.
    private const int AccessDeniedExitCode = 5;

    public async Task<PowerPlanActivationOutcome> TryActivatePerformanceSchemeAsync(
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            powerCfgPath,
            ["/SETACTIVE", "SCHEME_MIN"],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            return PowerPlanActivationOutcome.Activated;
        }

        return LooksLikeAccessDenied(result)
            ? PowerPlanActivationOutcome.AccessDenied
            : PowerPlanActivationOutcome.SchemeUnavailable;
    }

    private static bool LooksLikeAccessDenied(CommandResult result)
    {
        if (result.ExitCode == AccessDeniedExitCode)
        {
            return true;
        }

        var text = result.StandardError + result.StandardOutput;
        return text.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("acesso negado", StringComparison.OrdinalIgnoreCase);
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

    // SUB_PCIEXPRESS / ASPM_POLICY, documented Windows power setting GUIDs.
    private const string PciExpressSubgroupGuid = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string AspmPolicySettingGuid = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    public async Task<int?> GetPciExpressAspmPolicyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activeScheme = await GetActiveSchemeGuidAsync(cancellationToken).ConfigureAwait(false);
            if (activeScheme is null)
            {
                return null;
            }

            var status = PowerReadACValueIndex(
                IntPtr.Zero,
                activeScheme.Value,
                new Guid(PciExpressSubgroupGuid),
                new Guid(AspmPolicySettingGuid),
                out var value);

            return status == 0 ? value : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private async Task<Guid?> GetActiveSchemeGuidAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern int PowerReadACValueIndex(
        IntPtr rootPowerKey,
        Guid schemeGuid,
        Guid subGroupOfPowerSettings,
        Guid powerSetting,
        out int acValueIndex);

    public async Task<bool> TrySetPciExpressAspmPolicyAsync(int policyValue, CancellationToken cancellationToken)
    {
        if (policyValue is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(policyValue));
        }

        var indexText = policyValue.ToString(CultureInfo.InvariantCulture);
        var ac = await commandRunner.RunAsync(
            powerCfgPath,
            ["/setacvalueindex", "SCHEME_CURRENT", PciExpressSubgroupGuid, AspmPolicySettingGuid, indexText],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        var dc = await commandRunner.RunAsync(
            powerCfgPath,
            ["/setdcvalueindex", "SCHEME_CURRENT", PciExpressSubgroupGuid, AspmPolicySettingGuid, indexText],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        if (!ac.Succeeded || !dc.Succeeded)
        {
            return false;
        }

        var apply = await commandRunner.RunAsync(
            powerCfgPath,
            ["/S", "SCHEME_CURRENT"],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        return apply.Succeeded;
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
        if (!powerStatus.IsOnAcPower())
        {
            return WindowsActionApplyResult.NoChange(
                "O modo de alto desempenho não foi ativado porque o computador está na bateria.");
        }

        var previous = await controller.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var outcome = await controller.TryActivatePerformanceSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (outcome == PowerPlanActivationOutcome.AccessDenied)
        {
            // Muitas configurações do Windows permitem que um usuário comum
            // troque o plano de energia; só chegamos aqui quando o Windows
            // realmente recusou. Sem elevação, isso significa "precisa de
            // UAC" (o mecanismo de tentativa-sem-admin-primeiro trata essa
            // exceção de forma especial); já elevado, é um erro genuíno.
            throw new UnauthorizedAccessException(
                context.IsElevated
                    ? "O Windows recusou a troca do plano de energia mesmo com privilégios administrativos."
                    : "O modo de energia da sessão requer elevação.");
        }

        if (outcome == PowerPlanActivationOutcome.SchemeUnavailable)
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

internal sealed record PciExpressAspmSnapshot(int PreviousPolicy);

/// <summary>
/// Sets PCI Express Link State Power Management (ASPM) on the active power
/// scheme to "Off" (0) to reduce link-latency spikes during gaming,
/// documented and fully reversible via <c>powercfg /Q</c> and
/// <c>/set{a,d}cvalueindex</c> -- the same official mechanism
/// <see cref="SessionPerformancePowerPlanAction"/> already relies on for
/// scheme changes. Never touches any setting outside this one, documented
/// power-setting GUID pair.
/// </summary>
public sealed class PciExpressPowerManagementAction : WindowsOptimizationAction
{
    private const int OffPolicy = 0;

    private readonly IPowerPlanController controller;

    public PciExpressPowerManagementAction(IPowerPlanController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.AdjustPciExpressPowerManagement);

    public override async Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        var previous = await controller.GetPciExpressAspmPolicyAsync(cancellationToken).ConfigureAwait(false);
        if (previous is null)
        {
            return WindowsActionApplyResult.NoChange(
                "Este computador não expõe a configuração de PCI Express Link State Power Management.");
        }

        if (previous == OffPolicy)
        {
            return WindowsActionApplyResult.NoChange(
                "PCI Express Link State Power Management já estava desativado (Off).");
        }

        if (!await controller.TrySetPciExpressAspmPolicyAsync(OffPolicy, cancellationToken).ConfigureAwait(false))
        {
            return WindowsActionApplyResult.NoChange(
                "Não foi possível alterar o PCI Express Link State Power Management neste computador.");
        }

        return WindowsActionApplyResult.ChangedWith(
            new PciExpressAspmSnapshot(previous.Value),
            "PCI Express Link State Power Management definido como Off; o valor anterior foi salvo para rollback.");
    }

    public override async Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<PciExpressAspmSnapshot>(snapshotJson);
        // Uma falha de restauração precisa ser visível ao engine (que registra
        // o RollbackFailed no journal), não engolida: sem isso o histórico
        // reporta um rollback concluído que na verdade não restaurou nada.
        if (!await controller.TrySetPciExpressAspmPolicyAsync(snapshot.PreviousPolicy, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Não foi possível restaurar o PCI Express Link State Power Management para o valor anterior.");
        }
    }
}
