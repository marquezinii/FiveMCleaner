using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

internal sealed record LegacyGraphicsSnapshot(
    string SettingsPath,
    string BackupPath,
    string OriginalSha256,
    string AppliedSha256,
    IReadOnlyList<string> ChangedSettings);

public static class LegacyGraphicsPresets
{
    private static readonly IReadOnlyDictionary<string, string> Light =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MSAA"] = "0",
            ["MSAAFragments"] = "0",
            ["MSAAQuality"] = "0",
            ["ReflectionMSAA"] = "0",
            ["TXAA_Enabled"] = "false"
        };

    private static readonly IReadOnlyDictionary<string, string> Balanced =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MSAA"] = "0",
            ["MSAAFragments"] = "0",
            ["MSAAQuality"] = "0",
            ["ReflectionMSAA"] = "0",
            ["TXAA_Enabled"] = "false",
            ["ShadowQuality"] = "1",
            ["ReflectionQuality"] = "1",
            ["WaterQuality"] = "1",
            ["ParticlesQuality"] = "1",
            ["ParticleQuality"] = "1",
            ["GrassQuality"] = "1",
            ["ShaderQuality"] = "1",
            ["PostFX"] = "1",
            ["Tessellation"] = "1",
            ["SSAO"] = "1",
            ["AnisotropicFiltering"] = "8",
            ["CityDensity"] = "0.550000",
            ["PedVarietyMultiplier"] = "0.550000",
            ["VehicleVarietyMultiplier"] = "0.550000",
            ["DistanceScaling"] = "0.700000",
            ["LodScale"] = "0.700000",
            ["ExtendedDistanceScaling"] = "0.000000",
            ["ExtendedShadowDistance"] = "0.000000",
            ["LongShadows"] = "false",
            ["Shadow_LongShadows"] = "false",
            ["HighResolutionShadows"] = "false",
            ["UltraShadows_Enabled"] = "false",
            ["HighDetailStreamingWhileFlying"] = "false",
            ["HdStreamingInFlight"] = "false",
            ["DoF"] = "false",
            ["MotionBlurStrength"] = "0",
            ["MaxLodScale"] = "0"
        };

    private static readonly IReadOnlyDictionary<string, string> Aggressive =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MSAA"] = "0",
            ["MSAAFragments"] = "0",
            ["MSAAQuality"] = "0",
            ["ReflectionMSAA"] = "0",
            ["TXAA_Enabled"] = "false",
            ["ShadowQuality"] = "1",
            ["ReflectionQuality"] = "0",
            ["WaterQuality"] = "0",
            ["ParticlesQuality"] = "0",
            ["ParticleQuality"] = "0",
            ["GrassQuality"] = "0",
            ["ShaderQuality"] = "0",
            ["PostFX"] = "0",
            ["Tessellation"] = "0",
            ["SSAO"] = "0",
            ["AnisotropicFiltering"] = "4",
            ["TextureQuality"] = "1",
            ["CityDensity"] = "0.250000",
            ["PedVarietyMultiplier"] = "0.250000",
            ["VehicleVarietyMultiplier"] = "0.250000",
            ["DistanceScaling"] = "0.450000",
            ["LodScale"] = "0.450000",
            ["ExtendedDistanceScaling"] = "0.000000",
            ["ExtendedShadowDistance"] = "0.000000",
            ["LongShadows"] = "false",
            ["Shadow_LongShadows"] = "false",
            ["HighResolutionShadows"] = "false",
            ["UltraShadows_Enabled"] = "false",
            ["HighDetailStreamingWhileFlying"] = "false",
            ["HdStreamingInFlight"] = "false",
            ["Shadow_ParticleShadows"] = "false",
            ["Lighting_FogVolumes"] = "false",
            ["Shader_SSA"] = "false",
            ["DoF"] = "false",
            ["MotionBlurStrength"] = "0",
            ["MaxLodScale"] = "0"
        };

    public static IReadOnlyDictionary<string, string> For(OptimizationProfile profile)
    {
        return profile switch
        {
            OptimizationProfile.Light => Light,
            OptimizationProfile.Balanced => Balanced,
            OptimizationProfile.Aggressive => Aggressive,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
    }
}

public enum GraphicsSettingsTarget
{
    FiveM,
    GtaV
}

public sealed class LegacyGraphicsPresetAction : WindowsOptimizationAction
{
    private static readonly IReadOnlySet<string> AllowedSettingNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "FXAA",
            "MSAA",
            "MSAAFragments",
            "MSAAQuality",
            "ReflectionMSAA",
            "TXAA_Enabled",
            "ShadowQuality",
            "ReflectionQuality",
            "WaterQuality",
            "ParticlesQuality",
            "ParticleQuality",
            "GrassQuality",
            "ShaderQuality",
            "PostFX",
            "Tessellation",
            "SSAO",
            "AnisotropicFiltering",
            "TextureQuality",
            "CityDensity",
            "PedVarietyMultiplier",
            "VehicleVarietyMultiplier",
            "DistanceScaling",
            "LodScale",
            "ExtendedDistanceScaling",
            "ExtendedShadowDistance",
            "LongShadows",
            "Shadow_LongShadows",
            "HighResolutionShadows",
            "UltraShadows_Enabled",
            "HighDetailStreamingWhileFlying",
            "HdStreamingInFlight",
            "Shadow_ParticleShadows",
            "Lighting_FogVolumes",
            "Shader_SSA",
            "DoF",
            "MotionBlurStrength",
            "MaxLodScale"
        };

    private readonly string settingsPath;
    private readonly string? gameRoot;
    private readonly IReadOnlyDictionary<string, string> preset;
    private readonly IFiveMProcessInspector processInspector;
    private readonly IGtaVProcessInspector gtaVProcessInspector;
    private readonly GraphicsSettingsTarget target;

    public LegacyGraphicsPresetAction(
        string settingsPath,
        string fiveMRoot,
        OptimizationProfile profile,
        IFiveMProcessInspector processInspector)
        : this(
            settingsPath,
            fiveMRoot,
            profile,
            GraphicsSettingsTarget.FiveM,
            processInspector,
            new WindowsGtaVProcessInspector())
    {
    }

    public LegacyGraphicsPresetAction(
        string settingsPath,
        string? gameRoot,
        OptimizationProfile profile,
        GraphicsSettingsTarget target,
        IFiveMProcessInspector processInspector,
        IGtaVProcessInspector gtaVProcessInspector)
    {
        this.settingsPath = Path.GetFullPath(settingsPath);
        this.target = target;
        var expectedFileName = target == GraphicsSettingsTarget.FiveM
            ? "gta5_settings.xml"
            : "settings.xml";
        if (!Path.GetFileName(this.settingsPath).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"O alvo gráfico deve apontar para {expectedFileName}.",
                nameof(settingsPath));
        }

        this.gameRoot = string.IsNullOrWhiteSpace(gameRoot)
            ? null
            : SafePath.Normalize(gameRoot);
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
        this.gtaVProcessInspector = gtaVProcessInspector
            ?? throw new ArgumentNullException(nameof(gtaVProcessInspector));
        preset = LegacyGraphicsPresets.For(profile);
        Metadata = WindowsActionMetadata.For(GetActionId(target, profile));
        if (preset.Keys.Any(key => !AllowedSettingNames.Contains(key)))
        {
            throw new InvalidOperationException("Graphics preset contains a non-allowlisted setting.");
        }
    }

    public override ActionMetadataDto Metadata { get; }

    public override Task<WindowsActionApplyResult> ApplyAsync(
        WindowsActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target == GraphicsSettingsTarget.GtaV && gameRoot is null)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "A instalação do GTA V Legacy não foi confirmada; o settings.xml não será alterado."));
        }

        if (!File.Exists(settingsPath))
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                target == GraphicsSettingsTarget.FiveM
                    ? "gta5_settings.xml ainda não existe; abra o FiveM uma vez antes de aplicar o preset."
                    : "settings.xml ainda não existe; abra o GTA V Legacy uma vez antes de aplicar o preset."));
        }

        if (IsTargetRunning())
        {
            throw new InvalidOperationException(
                target == GraphicsSettingsTarget.FiveM
                    ? "FiveM precisa estar fechado para editar os gráficos."
                    : "GTA V precisa estar fechado para editar os gráficos.");
        }

        var (document, originalHash) = LoadSafeDocumentWithHash(settingsPath);
        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("Settings", StringComparison.Ordinal))
        {
            throw new InvalidDataException("O arquivo gráfico não possui uma raiz Settings reconhecida.");
        }

        var graphicsSections = root.Elements()
            .Where(element => element.Name.LocalName.Equals("graphics", StringComparison.Ordinal)
                && element.Name.Namespace == root.Name.Namespace)
            .ToArray();
        if (graphicsSections.Length != 1)
        {
            throw new InvalidDataException("O arquivo gráfico não possui uma seção graphics única.");
        }

        var graphics = graphicsSections[0];
        var changed = new List<string>();
        var incompatible = new List<string>();
        foreach (var setting in preset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodes = graphics
                .Elements()
                .Where(element => element.Name.LocalName.Equals(setting.Key, StringComparison.Ordinal)
                    && element.Name.Namespace == root.Name.Namespace)
                .ToArray();
            if (nodes.Length == 0)
            {
                continue;
            }

            if (nodes.Length != 1)
            {
                incompatible.Add(setting.Key);
                continue;
            }

            var attribute = nodes[0].Attribute("value");
            if (attribute is null || !IsCompatibleCurrentValue(setting.Key, attribute.Value))
            {
                incompatible.Add(setting.Key);
                continue;
            }

            if (!ShouldLowerValue(setting.Key, attribute.Value, setting.Value))
            {
                continue;
            }

            ValidatePresetValue(setting.Key, setting.Value);
            attribute.Value = setting.Value;
            changed.Add(setting.Key);
        }

        if (incompatible.Count > 0)
        {
            throw new InvalidDataException(
                $"O arquivo gráfico contém opções conhecidas incompatíveis: {string.Join(", ", incompatible.Distinct(StringComparer.Ordinal))}.");
        }

        if (changed.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "As configurações gráficas allowlisted já estavam no preset solicitado."));
        }

        var directory = Path.GetDirectoryName(settingsPath)!;
        var backupDirectory = Path.Combine(directory, ".fivemcleaner-backups");
        Directory.CreateDirectory(backupDirectory);
        if ((new DirectoryInfo(backupDirectory).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Graphics backup directory cannot be a reparse point.");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(settingsPath)}.{context.TransactionId:N}.tmp");
        var backupPath = Path.Combine(
            backupDirectory,
            $"{Path.GetFileNameWithoutExtension(settingsPath)}.{context.TransactionId:N}.bak");
        if (File.Exists(temporaryPath) || File.Exists(backupPath))
        {
            throw new IOException("A graphics transaction artifact already exists.");
        }

        try
        {
            SaveDocument(document, temporaryPath);
            _ = LoadSafeDocument(temporaryPath);
            var appliedHash = ComputeSha256(temporaryPath);
            if (IsTargetRunning())
            {
                throw new IOException("O jogo foi iniciado durante a preparação; nenhuma configuração foi substituída.");
            }

            if (!ComputeSha256(settingsPath).Equals(originalHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "As configurações gráficas mudaram durante a preparação; a gravação foi cancelada.");
            }

            ReplaceAndVerifyDisplacedOriginal(
                temporaryPath,
                settingsPath,
                backupPath,
                originalHash,
                "As configurações mudaram no instante da troca; a versão mais recente foi restaurada.");

            return Task.FromResult(WindowsActionApplyResult.ChangedWith(
                new LegacyGraphicsSnapshot(
                    settingsPath,
                    backupPath,
                    originalHash,
                    appliedHash,
                    changed),
                $"Backup criado e {changed.Count} opção(ões) gráfica(s) atualizada(s)."));
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<LegacyGraphicsSnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsTargetRunning())
        {
            throw new InvalidOperationException(
                target == GraphicsSettingsTarget.FiveM
                    ? "FiveM precisa estar fechado para restaurar os gráficos."
                    : "GTA V precisa estar fechado para restaurar os gráficos.");
        }

        var expectedBackupPath = Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            ".fivemcleaner-backups",
            $"{Path.GetFileNameWithoutExtension(settingsPath)}.{context.TransactionId:N}.bak");
        if (!Path.GetFullPath(snapshot.SettingsPath).Equals(settingsPath, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(snapshot.BackupPath).Equals(expectedBackupPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O snapshot gráfico aponta para caminhos inesperados.");
        }

        if (!File.Exists(expectedBackupPath))
        {
            throw new FileNotFoundException("Graphics backup is unavailable.", expectedBackupPath);
        }

        _ = LoadSafeDocument(expectedBackupPath);
        if (!ComputeSha256(expectedBackupPath).Equals(
                snapshot.OriginalSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O backup gráfico não corresponde ao snapshot original.");
        }

        if (!File.Exists(settingsPath))
        {
            if (IsTargetRunning())
            {
                throw new IOException("O jogo foi iniciado durante o rollback; nenhuma configuração foi restaurada.");
            }

            File.Copy(expectedBackupPath, settingsPath, overwrite: false);
            return Task.CompletedTask;
        }

        var currentHash = ComputeSha256(settingsPath);
        if (!currentHash.Equals(snapshot.AppliedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "Graphics settings changed after optimization; rollback refused to overwrite newer user edits.");
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            $".{Path.GetFileName(settingsPath)}.{context.TransactionId:N}.rollback.tmp");
        var displacedPath = Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            $".{Path.GetFileName(settingsPath)}.{context.TransactionId:N}.rollback-current.bak");
        if (File.Exists(temporaryPath) || File.Exists(displacedPath))
        {
            throw new IOException("Um artefato de rollback gráfico já existe.");
        }

        File.Copy(expectedBackupPath, temporaryPath, overwrite: false);
        try
        {
            _ = LoadSafeDocument(temporaryPath);
            if (IsTargetRunning())
            {
                throw new IOException("O jogo foi iniciado durante o rollback; nenhuma configuração foi substituída.");
            }

            if (!ComputeSha256(settingsPath).Equals(currentHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "As configurações gráficas mudaram durante o rollback; a restauração foi cancelada.");
            }

            ReplaceAndVerifyDisplacedOriginal(
                temporaryPath,
                settingsPath,
                displacedPath,
                currentHash,
                "As configurações mudaram no instante do rollback; a versão mais recente foi restaurada.");

            File.Delete(displacedPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return Task.CompletedTask;
    }

    private static XDocument LoadSafeDocument(string path)
    {
        return LoadSafeDocumentWithHash(path).Document;
    }

    private static (XDocument Document, string Sha256) LoadSafeDocumentWithHash(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            MaxCharactersInDocument = 4 * 1024 * 1024
        };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4 * 1024 * 1024)
        {
            throw new InvalidDataException("O arquivo gráfico excede o limite seguro de 4 MB.");
        }

        using var buffer = new MemoryStream((int)stream.Length);
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        buffer.Position = 0;
        using var reader = XmlReader.Create(buffer, settings);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return (document, hash);
    }

    private static void SaveDocument(XDocument document, string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = true
        };
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ReplaceAndVerifyDisplacedOriginal(
        string replacementPath,
        string destinationPath,
        string displacedPath,
        string expectedDisplacedSha256,
        string conflictMessage)
    {
        File.Replace(replacementPath, destinationPath, displacedPath, ignoreMetadataErrors: true);
        Exception? validationError = null;
        var matches = false;
        try
        {
            matches = ComputeSha256(displacedPath).Equals(
                expectedDisplacedSha256,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            validationError = exception;
        }

        if (matches)
        {
            return;
        }

        try
        {
            File.Replace(displacedPath, destinationPath, null, ignoreMetadataErrors: true);
        }
        catch (Exception restoreException) when (restoreException is IOException
            or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Não foi possível confirmar a troca; a versão deslocada ficou preservada em '{displacedPath}'.",
                new AggregateException(
                    validationError ?? new IOException("Hash deslocado divergente."),
                    restoreException));
        }

        throw new IOException(conflictMessage, validationError);
    }

    private static string GetActionId(
        GraphicsSettingsTarget target,
        OptimizationProfile profile)
    {
        return (target, profile) switch
        {
            (GraphicsSettingsTarget.FiveM, OptimizationProfile.Light) =>
                OptimizationActionIds.ApplyLightLegacyGraphics,
            (GraphicsSettingsTarget.FiveM, OptimizationProfile.Balanced) =>
                OptimizationActionIds.ApplyBalancedLegacyGraphics,
            (GraphicsSettingsTarget.FiveM, OptimizationProfile.Aggressive) =>
                OptimizationActionIds.ApplyAggressiveLegacyGraphics,
            (GraphicsSettingsTarget.GtaV, OptimizationProfile.Light) =>
                OptimizationActionIds.ApplyLightGtaVGraphics,
            (GraphicsSettingsTarget.GtaV, OptimizationProfile.Balanced) =>
                OptimizationActionIds.ApplyBalancedGtaVGraphics,
            (GraphicsSettingsTarget.GtaV, OptimizationProfile.Aggressive) =>
                OptimizationActionIds.ApplyAggressiveGtaVGraphics,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };
    }

    private bool IsTargetRunning()
    {
        return target == GraphicsSettingsTarget.FiveM
            ? processInspector.IsRunningFrom(gameRoot!)
            : gtaVProcessInspector.IsRunningFrom(gameRoot);
    }

    private static bool ShouldLowerValue(string name, string currentValue, string desiredValue)
    {
        ValidatePresetValue(name, desiredValue);
        if (IsBooleanSetting(name))
        {
            return bool.TryParse(currentValue, out var current)
                && bool.TryParse(desiredValue, out var desired)
                && current
                && !desired;
        }

        return decimal.TryParse(
                   currentValue,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out var currentNumber)
            && decimal.TryParse(
                desiredValue,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var desiredNumber)
            && currentNumber > desiredNumber;
    }

    private static bool IsCompatibleCurrentValue(string name, string value)
    {
        return IsBooleanSetting(name)
            ? bool.TryParse(value, out _)
            : decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out _);
    }

    private static bool IsBooleanSetting(string name)
    {
        return name is "FXAA" or "LongShadows" or "Shadow_LongShadows"
            or "HighResolutionShadows" or "UltraShadows_Enabled"
            or "HighDetailStreamingWhileFlying" or "HdStreamingInFlight"
            or "TXAA_Enabled" or "Shadow_ParticleShadows"
            or "Lighting_FogVolumes" or "Shader_SSA" or "DoF";
    }

    private static void ValidatePresetValue(string name, string value)
    {
        if (IsBooleanSetting(name))
        {
            if (!bool.TryParse(value, out _))
            {
                throw new InvalidOperationException($"'{value}' is not a valid boolean for '{name}'.");
            }

            return;
        }

        if (name is "CityDensity" or "PedVarietyMultiplier" or "VehicleVarietyMultiplier"
            or "DistanceScaling" or "LodScale" or "ExtendedDistanceScaling"
            or "ExtendedShadowDistance" or "MotionBlurStrength")
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                || number is < 0 or > 1)
            {
                throw new InvalidOperationException($"'{value}' is outside the safe range for '{name}'.");
            }

            return;
        }

        if (name == "AnisotropicFiltering")
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var filtering)
                || filtering is < 0 or > 16)
            {
                throw new InvalidOperationException($"'{value}' is outside the safe range for '{name}'.");
            }

            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            || integer is < 0 or > 4)
        {
            throw new InvalidOperationException($"'{value}' is outside the safe range for '{name}'.");
        }
    }
}
