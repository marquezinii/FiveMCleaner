using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ralven.Contracts;
using Ralven.Core.Catalog;
using Ralven.Windows.Infrastructure;

namespace Ralven.Windows.Actions;

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

    public static (IReadOnlyList<string> Lines, string Sha256) ReadLinesWithHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        buffer.Position = 0;
        using var reader = new StreamReader(
            buffer,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return (lines, Convert.ToHexString(SHA256.HashData(bytes)));
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

    public static string WriteAtomically(
        string path,
        IReadOnlyList<string> lines,
        bool originalExisted,
        string? expectedOriginalSha256)
    {
        var directory = Path.GetDirectoryName(path)!;
        SafePath.EnsureNoReparsePoints(directory);
        Directory.CreateDirectory(directory);
        var token = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{token}.tmp");
        var displacedPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{token}.apply-displaced");
        File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var appliedSha256 = SafeXmlDocumentStore.ComputeSha256(temporaryPath);
        var preserveDisplaced = false;
        try
        {
            // Revalidate immediately around the atomic exchange. Verifying the
            // displaced file closes the remaining TOCTOU window.
            SafePath.EnsureNoReparsePoints(path);
            if (originalExisted)
            {
                if (string.IsNullOrWhiteSpace(expectedOriginalSha256)
                    || !File.Exists(path)
                    || !SafeXmlDocumentStore.ComputeSha256(path).Equals(
                        expectedOriginalSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        "GTA V commandline.txt changed before optimization; the newer state was preserved.");
                }

                SafeXmlDocumentStore.ReplaceAndVerifyDisplacedOriginal(
                    temporaryPath,
                    path,
                    displacedPath,
                    expectedOriginalSha256,
                    "GTA V commandline.txt changed before optimization; the newer state was preserved.");
            }
            else
            {
                File.Move(temporaryPath, path, overwrite: false);
            }

            try
            {
                EnsureExpectedContents(path, lines);
            }
            catch (Exception validationError) when (validationError is IOException
                or UnauthorizedAccessException)
            {
                try
                {
                    CompensateFailedApply(
                        path,
                        originalExisted,
                        displacedPath,
                        appliedSha256);
                }
                catch (Exception compensationError) when (compensationError is IOException
                    or UnauthorizedAccessException)
                {
                    preserveDisplaced = File.Exists(displacedPath);
                    throw new IOException(
                        "GTA V commandline.txt failed postcondition and could not be restored safely.",
                        new AggregateException(validationError, compensationError));
                }

                throw;
            }

            if (File.Exists(displacedPath))
            {
                File.Delete(displacedPath);
            }

            return appliedSha256;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (!preserveDisplaced && File.Exists(displacedPath))
            {
                File.Delete(displacedPath);
            }
        }
    }

    internal static void CompensateFailedApply(
        string path,
        bool originalExisted,
        string displacedPath,
        string appliedSha256)
    {
        if (!File.Exists(path)
            || !SafeXmlDocumentStore.ComputeSha256(path).Equals(
                appliedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "GTA V commandline.txt changed after optimization; the newer state was preserved.");
        }

        if (originalExisted)
        {
            if (!File.Exists(displacedPath))
            {
                throw new IOException("The original GTA V commandline.txt is unavailable for compensation.");
            }

            File.Replace(displacedPath, path, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Delete(path);
        }
    }

    public static void EnsureExpectedContents(string path, IReadOnlyList<string> expectedLines)
    {
        if (!File.Exists(path)
            || !File.ReadAllLines(path).SequenceEqual(expectedLines, StringComparer.Ordinal))
        {
            throw new IOException("Windows did not persist the requested GTA V commandline parameters.");
        }
    }

    public static void RestoreAtomically(
        string path,
        bool originalExisted,
        IReadOnlyList<string> originalLines,
        string expectedCurrentSha256)
    {
        SafePath.EnsureNoReparsePoints(path);
        if (!File.Exists(path))
        {
            throw new IOException(
                "GTA V commandline.txt changed after optimization; rollback preserved the newer state.");
        }

        var directory = Path.GetDirectoryName(path)!;
        var token = Guid.NewGuid().ToString("N");
        var displacedPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{token}.rollback-displaced");
        if (!originalExisted)
        {
            File.Move(path, displacedPath, overwrite: false);
            if (SafeXmlDocumentStore.ComputeSha256(displacedPath).Equals(
                    expectedCurrentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(displacedPath);
                return;
            }

            if (!File.Exists(path))
            {
                File.Move(displacedPath, path, overwrite: false);
            }

            throw new IOException(
                "GTA V commandline.txt changed after optimization; rollback preserved the newer state.");
        }

        var replacementPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{token}.rollback.tmp");
        File.WriteAllLines(
            replacementPath,
            originalLines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            SafeXmlDocumentStore.ReplaceAndVerifyDisplacedOriginal(
                replacementPath,
                path,
                displacedPath,
                expectedCurrentSha256,
                "GTA V commandline.txt changed after optimization; rollback preserved the newer state.");
            File.Delete(displacedPath);
            EnsureExpectedContents(path, originalLines);
        }
        finally
        {
            if (File.Exists(replacementPath))
            {
                File.Delete(replacementPath);
            }
        }
    }
}

internal sealed record CommandLineSnapshot(
    bool OriginalExisted,
    IReadOnlyList<string> OriginalLines,
    string AppliedSha256,
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
            return Task.FromResult(WindowsActionApplyResult.Skipped(
                WindowsActionText.Format("ActionResults.GtaVCommandLine.NotConfirmedForDiagnosis")));
        }

        if (!File.Exists(commandLinePath))
        {
            return Task.FromResult(WindowsActionApplyResult.Skipped(
                WindowsActionText.Format("ActionResults.GtaVCommandLine.FileMissing")));
        }

        IReadOnlyList<string> lines;
        try
        {
            lines = GtaVCommandLineFile.ReadLines(commandLinePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(WindowsActionApplyResult.Skipped(
                WindowsActionText.Format("ActionResults.GtaVCommandLine.ReadFailed")));
        }

        var flags = lines
            .Select(GtaVCommandLineFile.FlagToken)
            .OfType<string>()
            .ToArray();
        var activeRepairFlags = RepairFlags
            .Where(repair => flags.Contains(repair, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var message = activeRepairFlags.Length > 0
            ? WindowsActionText.Format(
                "ActionResults.GtaVCommandLine.RepairFlagsActive",
                string.Join(", ", activeRepairFlags))
            : flags.Length > 0
                ? WindowsActionText.Format(
                    "ActionResults.GtaVCommandLine.RecognizedFlags",
                    flags.Length,
                    string.Join(", ", flags))
                : WindowsActionText.Format("ActionResults.GtaVCommandLine.NoRecognizedFlags");
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

    protected abstract string AppliedMessageKey { get; }

    public sealed override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commandLinePath is null)
        {
            return Task.FromResult(WindowsActionApplyResult.Skipped(
                WindowsActionText.Format("ActionResults.GtaVCommandLine.NotConfirmedForChange")));
        }

        if (processInspector.IsRunningFrom(gtaVInstallationRoot))
        {
            throw new InvalidOperationException("GTA V precisa estar fechado para editar commandline.txt.");
        }

        SafePath.EnsureNoReparsePoints(commandLinePath);
        var originalExisted = File.Exists(commandLinePath);
        IReadOnlyList<string> existingLines;
        string? originalSha256;
        if (originalExisted)
        {
            (existingLines, originalSha256) = GtaVCommandLineFile.ReadLinesWithHash(commandLinePath);
        }
        else
        {
            existingLines = [];
            originalSha256 = null;
        }

        var desired = BuildDesiredLines();
        var (mergedLines, changedFlags) = GtaVCommandLineFile.Merge(existingLines, ManagedFlags, desired);
        if (changedFlags.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                WindowsActionText.Format("ActionResults.GtaVCommandLine.AlreadyDesired")));
        }

        var appliedSha256 = GtaVCommandLineFile.WriteAtomically(
            commandLinePath,
            mergedLines,
            originalExisted,
            originalSha256);

        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            new CommandLineSnapshot(originalExisted, existingLines, appliedSha256, changedFlags),
            WindowsActionText.Format(AppliedMessageKey, string.Join(", ", changedFlags))));
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

        using var snapshotDocument = JsonDocument.Parse(snapshotJson);
        if (snapshotDocument.RootElement.TryGetProperty("settingsPath", out _)
            && !snapshotDocument.RootElement.TryGetProperty("appliedSha256", out _))
        {
            throw new InvalidDataException(
                "Este snapshot legado de commandline.txt não registra o estado aplicado; rollback recusado por segurança.");
        }

        var snapshot = WindowsActionSnapshot.Deserialize<CommandLineSnapshot>(snapshotJson);
        ValidateSnapshot(snapshot);
        GtaVCommandLineFile.RestoreAtomically(
            commandLinePath!,
            snapshot.OriginalExisted,
            snapshot.OriginalLines,
            snapshot.AppliedSha256);
        return Task.CompletedTask;
    }

    private void ValidateSnapshot(CommandLineSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.AppliedSha256))
        {
            throw new InvalidDataException(
                "Este snapshot legado de commandline.txt não registra o estado aplicado; rollback recusado por segurança.");
        }

        if (snapshot.OriginalLines is null
            || snapshot.ChangedFlags is null
            || snapshot.ChangedFlags.Count == 0
            || snapshot.ChangedFlags.Any(flag => !ManagedFlags.Contains(flag))
            || snapshot.AppliedSha256.Length != 64
            || snapshot.AppliedSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("The GTA V commandline snapshot is invalid for this action.");
        }
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

    protected override string AppliedMessageKey => "ActionResults.GtaVCommandLine.GraphicsApplied";

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

    protected override string AppliedMessageKey => "ActionResults.GtaVCommandLine.DisplayApplied";

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

    protected override string AppliedMessageKey => "ActionResults.GtaVCommandLine.RepairApplied";

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
