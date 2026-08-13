using System.Globalization;
using System.Xml.Linq;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

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

    /// <summary>
    /// Raises (never lowers) existing options up to a conservative ceiling.
    /// Deliberately does not touch MSAA/ReflectionMSAA/TXAA (GPU-dependent
    /// cost too variable to guess safely), extended distance/shadow settings,
    /// motion blur or depth of field, so a heavy 1% low regression is not
    /// silently introduced.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Quality =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FXAA"] = "true",
            ["ShadowQuality"] = "2",
            ["ReflectionQuality"] = "2",
            ["WaterQuality"] = "2",
            ["ParticlesQuality"] = "2",
            ["ParticleQuality"] = "2",
            ["GrassQuality"] = "2",
            ["ShaderQuality"] = "2",
            ["PostFX"] = "2",
            ["Tessellation"] = "1",
            ["SSAO"] = "1",
            ["AnisotropicFiltering"] = "16",
            ["TextureQuality"] = "2",
            ["CityDensity"] = "1.000000",
            ["PedVarietyMultiplier"] = "1.000000",
            ["VehicleVarietyMultiplier"] = "1.000000",
            ["DistanceScaling"] = "1.000000",
            ["LodScale"] = "1.000000"
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

/// <summary>
/// Whether a preset is only allowed to lower existing values (the original,
/// most conservative behavior) or only allowed to raise them (used
/// exclusively by the opt-in quality preset, which never lowers a setting
/// the user already has above the preset's target).
/// </summary>
public enum GraphicsPresetDirection
{
    LowerOnly,
    RaiseOnly
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
    private static readonly SafeXmlTransactionMessages TransactionMessages = new(
        "Graphics backup directory cannot be a reparse point.",
        "A graphics transaction artifact already exists.",
        "O arquivo gráfico excede o limite seguro de 4 MB.",
        "As configurações gráficas mudaram durante a preparação; a gravação foi cancelada.",
        "As configurações mudaram no instante da troca; a versão mais recente foi restaurada.",
        "O snapshot gráfico aponta para caminhos inesperados.",
        "Graphics backup is unavailable.",
        "O backup gráfico não corresponde ao snapshot original.",
        "Graphics settings changed after optimization; rollback refused to overwrite newer user edits.",
        "Um artefato de rollback gráfico já existe.",
        "As configurações gráficas mudaram durante o rollback; a restauração foi cancelada.",
        "As configurações mudaram no instante do rollback; a versão mais recente foi restaurada.");

    private readonly string settingsPath;
    private readonly string? gameRoot;
    private readonly IReadOnlyDictionary<string, string> preset;
    private readonly GraphicsTargetProcessGuard processGuard;
    private readonly SafeXmlSettingsTransaction transaction;
    private readonly GraphicsSettingsTarget target;
    private readonly GraphicsPresetDirection direction;

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
        : this(
            settingsPath,
            gameRoot,
            target,
            processInspector,
            gtaVProcessInspector,
            GetActionId(target, profile),
            LegacyGraphicsPresets.For(profile),
            GraphicsPresetDirection.LowerOnly)
    {
    }

    public LegacyGraphicsPresetAction(
        string settingsPath,
        string? gameRoot,
        GraphicsSettingsTarget target,
        IFiveMProcessInspector processInspector,
        IGtaVProcessInspector gtaVProcessInspector,
        string actionId,
        IReadOnlyDictionary<string, string> preset,
        GraphicsPresetDirection direction)
    {
        this.settingsPath = Path.GetFullPath(settingsPath);
        this.target = target;
        this.direction = direction;
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
        processGuard = new GraphicsTargetProcessGuard(
            target,
            this.gameRoot,
            processInspector,
            gtaVProcessInspector);
        transaction = new SafeXmlSettingsTransaction(
            this.settingsPath,
            string.Empty,
            TransactionMessages,
            processGuard.IsRunning);
        this.preset = preset ?? throw new ArgumentNullException(nameof(preset));
        Metadata = WindowsActionMetadata.For(actionId);
        if (this.preset.Keys.Any(key => !AllowedSettingNames.Contains(key)))
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

        processGuard.EnsureStopped(
            "FiveM precisa estar fechado para editar os gráficos.",
            "GTA V precisa estar fechado para editar os gráficos.");

        var (document, originalHash) = SafeXmlDocumentStore.LoadSafeDocumentWithHash(
            settingsPath,
            "O arquivo gráfico excede o limite seguro de 4 MB.");
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

            var shouldChange = direction == GraphicsPresetDirection.LowerOnly
                ? ShouldLowerValue(setting.Key, attribute.Value, setting.Value)
                : ShouldRaiseValue(setting.Key, attribute.Value, setting.Value);
            if (!shouldChange)
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

        var snapshot = transaction.Apply(document, context.TransactionId, originalHash, changed);
        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            snapshot,
            $"Backup criado e {changed.Count} opção(ões) gráfica(s) atualizada(s)."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<SafeXmlSettingsSnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        processGuard.EnsureStopped(
            "FiveM precisa estar fechado para restaurar os gráficos.",
            "GTA V precisa estar fechado para restaurar os gráficos.");
        transaction.Rollback(context.TransactionId, snapshot);
        return Task.CompletedTask;
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

    private static bool ShouldRaiseValue(string name, string currentValue, string desiredValue)
    {
        ValidatePresetValue(name, desiredValue);
        if (IsBooleanSetting(name))
        {
            return bool.TryParse(currentValue, out var current)
                && bool.TryParse(desiredValue, out var desired)
                && !current
                && desired;
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
            && currentNumber < desiredNumber;
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
