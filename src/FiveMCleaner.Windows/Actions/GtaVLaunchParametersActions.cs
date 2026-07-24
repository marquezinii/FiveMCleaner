using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

/// <summary>
/// Shared read/merge/write mechanics for GTA V standalone's
/// <c>commandline.txt</c> (Rockstar-documented launch parameters). This file
/// has no effect on FiveM: FiveM explicitly blocks reading commandline.txt
/// from the GTA install (see docs/research.md, citing FiveM's own
/// BlockLoadSetters.cpp), so every action here only ever targets the
/// standalone GTA V executable's folder, never a FiveM path.
///
/// Only lines whose flag token is in the caller's managed set are ever
/// touched; every other line (including flags this product does not know
/// about) is preserved exactly as-is, the same allowlist-only philosophy
/// used by the graphics XML actions.
/// </summary>
internal static class GtaVCommandLineFile
{
    public static IReadOnlyList<string> ReadLines(string path)
    {
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    public static string? FlagToken(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var spaceIndex = trimmed.IndexOfAny([' ', '\t']);
        return spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
    }

    public static (IReadOnlyList<string> Lines, IReadOnlyList<string> ChangedFlags) Merge(
        IReadOnlyList<string> existingLines,
        IReadOnlySet<string> managedFlags,
        IReadOnlyList<string> desiredManagedLines)
    {
        var kept = existingLines
            .Where(line => FlagToken(line) is not { } flag || !managedFlags.Contains(flag))
            .ToArray();
        var existingManaged = existingLines
            .Where(line => FlagToken(line) is { } flag && managedFlags.Contains(flag))
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var desiredOrdered = desiredManagedLines
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (existingManaged.SequenceEqual(desiredOrdered, StringComparer.OrdinalIgnoreCase))
        {
            return (existingLines, []);
        }

        var changedFlags = existingManaged
            .Select(FlagToken)
            .Concat(desiredManagedLines.Select(FlagToken))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (kept.Concat(desiredManagedLines).ToArray(), changedFlags);
    }

    public static string ComputeSha256(IReadOnlyList<string> lines)
    {
        var text = string.Join('\n', lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static void WriteAtomically(string path, IReadOnlyList<string> lines)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

internal sealed record CommandLineSnapshot(
    string SettingsPath,
    IReadOnlyList<string> OriginalLines,
    IReadOnlyList<string> ChangedFlags);

public sealed class GtaVLaunchParametersDiagnosisAction : WindowsOptimizationAction
{
    private static readonly string[] RepairFlags = ["-safemode", "-useMinimumSettings", "-UseAutoSettings"];
    private readonly string? commandLinePath;

    public GtaVLaunchParametersDiagnosisAction(string? gtaVInstallationRoot)
    {
        commandLinePath = string.IsNullOrWhiteSpace(gtaVInstallationRoot)
            ? null
            : Path.Combine(SafePath.Normalize(gtaVInstallationRoot), "commandline.txt");
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.DiagnoseGtaVLaunchParameters);

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commandLinePath is null)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "A instalação do GTA V Legacy standalone não foi confirmada; nada para diagnosticar."));
        }

        if (!File.Exists(commandLinePath))
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Nenhum commandline.txt foi encontrado na pasta do GTA V; o jogo está usando os parâmetros padrão."));
        }

        IReadOnlyList<string> lines;
        try
        {
            lines = GtaVCommandLineFile.ReadLines(commandLinePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Não foi possível ler o commandline.txt ({exception.Message})."));
        }

        var flags = lines
            .Select(GtaVCommandLineFile.FlagToken)
            .OfType<string>()
            .ToArray();
        var activeRepairFlags = RepairFlags
            .Where(repair => flags.Contains(repair, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var message = activeRepairFlags.Length > 0
            ? $"Atenção: parâmetro(s) de reparo ainda ativo(s) no commandline.txt do GTA V: {string.Join(", ", activeRepairFlags)}. "
                + "Isso não deveria ficar permanente; reverta assim que o problema for diagnosticado."
            : flags.Length > 0
                ? $"{flags.Length} parâmetro(s) reconhecido(s) no commandline.txt do GTA V: {string.Join(", ", flags)}."
                : "O commandline.txt existe mas não contém parâmetros reconhecidos.";
        return Task.FromResult(WindowsActionApplyResult.NoChange(message));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public abstract class GtaVLaunchParametersActionBase : WindowsOptimizationAction
{
    private readonly string? gtaVInstallationRoot;
    private readonly string? commandLinePath;
    private readonly IGtaVProcessInspector processInspector;

    protected GtaVLaunchParametersActionBase(
        string? gtaVInstallationRoot,
        IGtaVProcessInspector processInspector)
    {
        this.gtaVInstallationRoot = string.IsNullOrWhiteSpace(gtaVInstallationRoot)
            ? null
            : SafePath.Normalize(gtaVInstallationRoot);
        commandLinePath = this.gtaVInstallationRoot is null
            ? null
            : Path.Combine(this.gtaVInstallationRoot, "commandline.txt");
        this.processInspector = processInspector ?? throw new ArgumentNullException(nameof(processInspector));
    }

    protected abstract IReadOnlySet<string> ManagedFlags { get; }

    protected abstract IReadOnlyList<string> BuildDesiredLines();

    protected abstract string NoticeVerb { get; }

    public sealed override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commandLinePath is null)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "A instalação do GTA V Legacy standalone não foi confirmada; commandline.txt não será alterado."));
        }

        if (processInspector.IsRunningFrom(gtaVInstallationRoot))
        {
            throw new InvalidOperationException("GTA V precisa estar fechado para editar commandline.txt.");
        }

        IReadOnlyList<string> existingLines;
        try
        {
            existingLines = GtaVCommandLineFile.ReadLines(commandLinePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                $"Não foi possível ler o commandline.txt ({exception.Message})."));
        }

        var desired = BuildDesiredLines();
        var (mergedLines, changedFlags) = GtaVCommandLineFile.Merge(existingLines, ManagedFlags, desired);
        if (changedFlags.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Os parâmetros gerenciados já estavam na configuração desejada."));
        }

        GtaVCommandLineFile.WriteAtomically(commandLinePath, mergedLines);

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new CommandLineSnapshot(commandLinePath, existingLines, changedFlags),
            $"{NoticeVerb}: {string.Join(", ", changedFlags)}."));
    }

    public sealed override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return Task.CompletedTask;
        }

        if (processInspector.IsRunningFrom(gtaVInstallationRoot))
        {
            throw new InvalidOperationException("GTA V precisa estar fechado para restaurar commandline.txt.");
        }

        var snapshot = WindowsActionSnapshot.Deserialize<CommandLineSnapshot>(snapshotJson);
        GtaVCommandLineFile.WriteAtomically(snapshot.SettingsPath, snapshot.OriginalLines);
        return Task.CompletedTask;
    }
}

public sealed class GtaVGraphicsLaunchParametersAction : GtaVLaunchParametersActionBase
{
    private static readonly IReadOnlySet<string> Managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-cityDensity", "-anisotropicQualityLevel", "-fxaa", "-grassQuality", "-lodScale", "-frameLimit"
    };

    private readonly IDisplayConfigurationInspector displayConfiguration;

    public GtaVGraphicsLaunchParametersAction(
        string? gtaVInstallationRoot,
        IDisplayConfigurationInspector displayConfiguration,
        IGtaVProcessInspector processInspector)
        : base(gtaVInstallationRoot, processInspector)
    {
        this.displayConfiguration = displayConfiguration
            ?? throw new ArgumentNullException(nameof(displayConfiguration));
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ApplyGtaVGraphicsLaunchParameters);

    protected override IReadOnlySet<string> ManagedFlags => Managed;

    protected override string NoticeVerb => "Parâmetro(s) gráfico(s) de inicialização atualizado(s)";

    protected override IReadOnlyList<string> BuildDesiredLines()
    {
        var lines = new List<string>
        {
            "-cityDensity 0.550000",
            "-anisotropicQualityLevel 8",
            "-fxaa",
            "-grassQuality 1",
            "-lodScale 0.700000"
        };

        var refreshHz = displayConfiguration.GetSnapshot()?.CurrentRefreshHz;
        if (refreshHz is > 0)
        {
            lines.Add($"-frameLimit {refreshHz.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return lines;
    }
}

public sealed class GtaVDisplayLaunchParametersAction : GtaVLaunchParametersActionBase
{
    private static readonly IReadOnlySet<string> Managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-fullscreen", "-windowed", "-borderless", "-DX10", "-DX10_1", "-DX11"
    };

    private readonly bool preferWindowedMode;
    private readonly bool preferBorderlessWindow;
    private readonly GtaVDirectXVersion directXVersion;

    public GtaVDisplayLaunchParametersAction(
        string? gtaVInstallationRoot,
        bool preferWindowedMode,
        bool preferBorderlessWindow,
        GtaVDirectXVersion directXVersion,
        IGtaVProcessInspector processInspector)
        : base(gtaVInstallationRoot, processInspector)
    {
        this.preferWindowedMode = preferWindowedMode;
        this.preferBorderlessWindow = preferBorderlessWindow;
        this.directXVersion = directXVersion;
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ApplyGtaVDisplayLaunchParameters);

    protected override IReadOnlySet<string> ManagedFlags => Managed;

    protected override string NoticeVerb => "Parâmetro(s) de exibição de inicialização atualizado(s)";

    protected override IReadOnlyList<string> BuildDesiredLines()
    {
        var lines = new List<string>
        {
            preferBorderlessWindow ? "-borderless" : preferWindowedMode ? "-windowed" : "-fullscreen"
        };

        switch (directXVersion)
        {
            case GtaVDirectXVersion.DX10:
                lines.Add("-DX10");
                break;
            case GtaVDirectXVersion.DX10_1:
                lines.Add("-DX10_1");
                break;
            case GtaVDirectXVersion.DX11:
                lines.Add("-DX11");
                break;
            case GtaVDirectXVersion.Unspecified:
            default:
                // No DX flag written: the game auto-detects, same as never having set one.
                break;
        }

        return lines;
    }
}

public sealed class GtaVRepairLaunchParametersAction : GtaVLaunchParametersActionBase
{
    private static readonly IReadOnlySet<string> Managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "-safemode", "-useMinimumSettings", "-UseAutoSettings"
    };

    private readonly bool useSafeMode;
    private readonly bool useMinimumSettings;
    private readonly bool useAutoSettingsRebuild;

    public GtaVRepairLaunchParametersAction(
        string? gtaVInstallationRoot,
        bool useSafeMode,
        bool useMinimumSettings,
        bool useAutoSettingsRebuild,
        IGtaVProcessInspector processInspector)
        : base(gtaVInstallationRoot, processInspector)
    {
        this.useSafeMode = useSafeMode;
        this.useMinimumSettings = useMinimumSettings;
        this.useAutoSettingsRebuild = useAutoSettingsRebuild;
    }

    public override ActionMetadataDto Metadata { get; } = WindowsActionMetadata.For(
        OptimizationActionIds.ApplyGtaVRepairLaunchParameters);

    protected override IReadOnlySet<string> ManagedFlags => Managed;

    protected override string NoticeVerb => "Parâmetro(s) de reparo temporariamente ativado(s) — lembre-se de reverter";

    protected override IReadOnlyList<string> BuildDesiredLines()
    {
        var lines = new List<string>();
        if (useSafeMode)
        {
            lines.Add("-safemode");
        }

        if (useMinimumSettings)
        {
            lines.Add("-useMinimumSettings");
        }

        if (useAutoSettingsRebuild)
        {
            lines.Add("-UseAutoSettings");
        }

        return lines;
    }
}
