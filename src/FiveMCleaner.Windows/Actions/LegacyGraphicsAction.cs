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
            ["ReflectionMSAA"] = "0",
            ["ExtendedDistanceScaling"] = "0.150000",
            ["ExtendedShadowDistance"] = "0.000000"
        };

    private static readonly IReadOnlyDictionary<string, string> Balanced =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FXAA"] = "true",
            ["MSAA"] = "0",
            ["ReflectionMSAA"] = "0",
            ["ShadowQuality"] = "1",
            ["ReflectionQuality"] = "1",
            ["WaterQuality"] = "1",
            ["ParticlesQuality"] = "1",
            ["GrassQuality"] = "1",
            ["ShaderQuality"] = "1",
            ["PostFX"] = "1",
            ["Tessellation"] = "0",
            ["SSAO"] = "0",
            ["CityDensity"] = "0.500000",
            ["PedVarietyMultiplier"] = "0.500000",
            ["VehicleVarietyMultiplier"] = "0.500000",
            ["DistanceScaling"] = "0.500000",
            ["ExtendedDistanceScaling"] = "0.000000",
            ["ExtendedShadowDistance"] = "0.000000",
            ["LongShadows"] = "false",
            ["HighResolutionShadows"] = "false",
            ["HighDetailStreamingWhileFlying"] = "false"
        };

    private static readonly IReadOnlyDictionary<string, string> Aggressive =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FXAA"] = "true",
            ["MSAA"] = "0",
            ["ReflectionMSAA"] = "0",
            ["ShadowQuality"] = "0",
            ["ReflectionQuality"] = "0",
            ["WaterQuality"] = "0",
            ["ParticlesQuality"] = "0",
            ["GrassQuality"] = "0",
            ["ShaderQuality"] = "0",
            ["PostFX"] = "0",
            ["Tessellation"] = "0",
            ["SSAO"] = "0",
            ["AnisotropicFiltering"] = "0",
            ["TextureQuality"] = "0",
            ["CityDensity"] = "0.250000",
            ["PedVarietyMultiplier"] = "0.250000",
            ["VehicleVarietyMultiplier"] = "0.250000",
            ["DistanceScaling"] = "0.250000",
            ["ExtendedDistanceScaling"] = "0.000000",
            ["ExtendedShadowDistance"] = "0.000000",
            ["LongShadows"] = "false",
            ["HighResolutionShadows"] = "false",
            ["HighDetailStreamingWhileFlying"] = "false"
        };

    public static IReadOnlyDictionary<string, string> For(OptimizationProfile profile)
    {
        return profile switch
        {
            OptimizationProfile.Balanced => Balanced,
            OptimizationProfile.Aggressive => Aggressive,
            _ => throw new ArgumentOutOfRangeException(
                nameof(profile),
                profile,
                "Only Balanced and Aggressive have a graphics action in the Core catalog.")
        };
    }
}

public sealed class LegacyGraphicsPresetAction : WindowsOptimizationAction
{
    private static readonly IReadOnlySet<string> AllowedSettingNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "FXAA",
            "MSAA",
            "ReflectionMSAA",
            "ShadowQuality",
            "ReflectionQuality",
            "WaterQuality",
            "ParticlesQuality",
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
            "ExtendedDistanceScaling",
            "ExtendedShadowDistance",
            "LongShadows",
            "HighResolutionShadows",
            "HighDetailStreamingWhileFlying"
        };

    private readonly string settingsPath;
    private readonly string fiveMRoot;
    private readonly IReadOnlyDictionary<string, string> preset;
    private readonly IFiveMProcessInspector processInspector;

    public LegacyGraphicsPresetAction(
        string settingsPath,
        string fiveMRoot,
        OptimizationProfile profile,
        IFiveMProcessInspector processInspector)
    {
        this.settingsPath = Path.GetFullPath(settingsPath);
        if (!Path.GetFileName(this.settingsPath).Equals(
            "gta5_settings.xml",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only CitizenFX gta5_settings.xml can be edited.",
                nameof(settingsPath));
        }

        this.fiveMRoot = SafePath.Normalize(fiveMRoot);
        this.processInspector = processInspector
            ?? throw new ArgumentNullException(nameof(processInspector));
        preset = LegacyGraphicsPresets.For(profile);
        Metadata = WindowsActionMetadata.For(profile switch
        {
            OptimizationProfile.Balanced => OptimizationActionIds.ApplyBalancedLegacyGraphics,
            OptimizationProfile.Aggressive => OptimizationActionIds.ApplyAggressiveLegacyGraphics,
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        });
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
        if (!File.Exists(settingsPath))
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "gta5_settings.xml ainda não existe; abra o FiveM uma vez antes de aplicar o preset."));
        }

        if (processInspector.IsRunningFrom(fiveMRoot))
        {
            throw new InvalidOperationException("FiveM precisa estar fechado para editar os gráficos.");
        }

        var document = LoadSafeDocument(settingsPath);
        var changed = new List<string>();
        foreach (var setting in preset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodes = document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals(setting.Key, StringComparison.Ordinal))
                .ToArray();
            if (nodes.Length != 1)
            {
                continue;
            }

            var attribute = nodes[0].Attribute("value");
            if (attribute is null || attribute.Value.Equals(setting.Value, StringComparison.Ordinal))
            {
                continue;
            }

            ValidatePresetValue(setting.Key, setting.Value);
            attribute.Value = setting.Value;
            changed.Add(setting.Key);
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
            $"gta5_settings.{context.TransactionId:N}.bak");
        if (File.Exists(temporaryPath) || File.Exists(backupPath))
        {
            throw new IOException("A graphics transaction artifact already exists.");
        }

        var originalHash = ComputeSha256(settingsPath);
        try
        {
            SaveDocument(document, temporaryPath);
            _ = LoadSafeDocument(temporaryPath);
            var appliedHash = ComputeSha256(temporaryPath);
            File.Replace(temporaryPath, settingsPath, backupPath, ignoreMetadataErrors: true);
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

        if (!File.Exists(snapshot.BackupPath))
        {
            throw new FileNotFoundException("Graphics backup is unavailable.", snapshot.BackupPath);
        }

        if (!File.Exists(snapshot.SettingsPath))
        {
            File.Move(snapshot.BackupPath, snapshot.SettingsPath, overwrite: false);
            return Task.CompletedTask;
        }

        var currentHash = ComputeSha256(snapshot.SettingsPath);
        if (!currentHash.Equals(snapshot.AppliedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "Graphics settings changed after optimization; rollback refused to overwrite newer user edits.");
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(snapshot.SettingsPath)!,
            $".{Path.GetFileName(snapshot.SettingsPath)}.{context.TransactionId:N}.rollback.tmp");
        File.Copy(snapshot.BackupPath, temporaryPath, overwrite: false);
        try
        {
            _ = LoadSafeDocument(temporaryPath);
            File.Replace(temporaryPath, snapshot.SettingsPath, null, ignoreMetadataErrors: true);
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
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            MaxCharactersInDocument = 4 * 1024 * 1024
        };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
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

    private static void ValidatePresetValue(string name, string value)
    {
        if (name is "FXAA" or "LongShadows" or "HighResolutionShadows"
            or "HighDetailStreamingWhileFlying")
        {
            if (!bool.TryParse(value, out _))
            {
                throw new InvalidOperationException($"'{value}' is not a valid boolean for '{name}'.");
            }

            return;
        }

        if (name is "CityDensity" or "PedVarietyMultiplier" or "VehicleVarietyMultiplier"
            or "DistanceScaling" or "ExtendedDistanceScaling" or "ExtendedShadowDistance")
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                || number is < 0 or > 1)
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
