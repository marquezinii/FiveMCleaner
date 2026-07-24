using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

internal sealed record DisplayPreferencesSnapshot(
    string SettingsPath,
    string BackupPath,
    string OriginalSha256,
    string AppliedSha256,
    IReadOnlyList<string> ChangedSettings);

/// <summary>
/// Writes only windowed mode and VSync to the existing gta5_settings.xml or
/// settings.xml, reusing the same backup/hash/atomic-replace safety
/// mechanics as <see cref="LegacyGraphicsPresetAction"/>. Deliberately never
/// touches resolution, refresh rate, adapter index or aspect ratio: those
/// require validating the target mode against the monitor's actually
/// supported modes before writing, which this product does not yet do
/// automatically (see docs/safety.md and PROJECT_STATE.md).
/// </summary>
public sealed class DisplayPreferencesAction : WindowsOptimizationAction
{
    private static readonly IReadOnlySet<string> AllowedSettingNames =
        new HashSet<string>(StringComparer.Ordinal) { "Windowed", "VSync" };

    private readonly string settingsPath;
    private readonly string? gameRoot;
    private readonly IReadOnlyDictionary<string, bool> preferences;
    private readonly IFiveMProcessInspector processInspector;
    private readonly IGtaVProcessInspector gtaVProcessInspector;
    private readonly GraphicsSettingsTarget target;

    public DisplayPreferencesAction(
        string settingsPath,
        string? gameRoot,
        GraphicsSettingsTarget target,
        bool preferWindowedMode,
        bool enableVSync,
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
                $"O alvo de exibição deve apontar para {expectedFileName}.",
                nameof(settingsPath));
        }

        this.gameRoot = string.IsNullOrWhiteSpace(gameRoot) ? null : SafePath.Normalize(gameRoot);
        this.processInspector = processInspector ?? throw new ArgumentNullException(nameof(processInspector));
        this.gtaVProcessInspector = gtaVProcessInspector
            ?? throw new ArgumentNullException(nameof(gtaVProcessInspector));
        preferences = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Windowed"] = preferWindowedMode,
            ["VSync"] = enableVSync
        };
        Metadata = WindowsActionMetadata.For(
            target == GraphicsSettingsTarget.FiveM
                ? OptimizationActionIds.ApplyLegacyDisplayPreferences
                : OptimizationActionIds.ApplyGtaVDisplayPreferences);
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
                    ? "gta5_settings.xml ainda não existe; abra o FiveM uma vez antes de aplicar a preferência."
                    : "settings.xml ainda não existe; abra o GTA V Legacy uma vez antes de aplicar a preferência."));
        }

        if (IsTargetRunning())
        {
            throw new InvalidOperationException(
                target == GraphicsSettingsTarget.FiveM
                    ? "FiveM precisa estar fechado para editar a exibição."
                    : "GTA V precisa estar fechado para editar a exibição.");
        }

        var (document, originalHash) = LoadSafeDocumentWithHash(settingsPath);
        var root = document.Root;
        if (root is null)
        {
            throw new InvalidDataException("O arquivo de exibição não possui uma raiz reconhecida.");
        }

        var changed = new List<string>();
        foreach (var preference in preferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AllowedSettingNames.Contains(preference.Key))
            {
                throw new InvalidOperationException("Display preference contains a non-allowlisted setting.");
            }

            var nodes = root
                .Descendants()
                .Where(element => element.Name.LocalName.Equals(preference.Key, StringComparison.Ordinal))
                .ToArray();
            if (nodes.Length == 0)
            {
                continue;
            }

            if (nodes.Length != 1)
            {
                // Ambiguous location: skip rather than guess which node governs display mode.
                continue;
            }

            var attribute = nodes[0].Attribute("value");
            if (attribute is null || !TryParseFlexibleBoolean(attribute.Value, out var current))
            {
                continue;
            }

            if (current == preference.Value)
            {
                continue;
            }

            attribute.Value = FormatFlexibleBoolean(preference.Value, attribute.Value);
            changed.Add(preference.Key);
        }

        if (changed.Count == 0)
        {
            return Task.FromResult(WindowsActionApplyResult.NoChange(
                "Janela e VSync já estavam na preferência solicitada, ou não foram encontrados no arquivo."));
        }

        var directory = Path.GetDirectoryName(settingsPath)!;
        var backupDirectory = Path.Combine(directory, ".fivemcleaner-backups");
        Directory.CreateDirectory(backupDirectory);
        if ((new DirectoryInfo(backupDirectory).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Display preferences backup directory cannot be a reparse point.");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(settingsPath)}.{context.TransactionId:N}.display.tmp");
        var backupPath = Path.Combine(
            backupDirectory,
            $"{Path.GetFileNameWithoutExtension(settingsPath)}.{context.TransactionId:N}.display.bak");
        if (File.Exists(temporaryPath) || File.Exists(backupPath))
        {
            throw new IOException("A display preferences transaction artifact already exists.");
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
                    "As configurações de exibição mudaram durante a preparação; a gravação foi cancelada.");
            }

            ReplaceAndVerifyDisplacedOriginal(
                temporaryPath,
                settingsPath,
                backupPath,
                originalHash,
                "As configurações de exibição mudaram no instante da troca; a versão mais recente foi restaurada.");

            return Task.FromResult(WindowsActionApplyResult.ChangedWith(
                new DisplayPreferencesSnapshot(
                    settingsPath,
                    backupPath,
                    originalHash,
                    appliedHash,
                    changed),
                $"Backup criado e {changed.Count} preferência(s) de exibição atualizada(s)."));
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
        var snapshot = WindowsActionSnapshot.Deserialize<DisplayPreferencesSnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsTargetRunning())
        {
            throw new InvalidOperationException(
                target == GraphicsSettingsTarget.FiveM
                    ? "FiveM precisa estar fechado para restaurar a exibição."
                    : "GTA V precisa estar fechado para restaurar a exibição.");
        }

        var expectedBackupPath = Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            ".fivemcleaner-backups",
            $"{Path.GetFileNameWithoutExtension(settingsPath)}.{context.TransactionId:N}.display.bak");
        if (!Path.GetFullPath(snapshot.SettingsPath).Equals(settingsPath, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(snapshot.BackupPath).Equals(expectedBackupPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O snapshot de exibição aponta para caminhos inesperados.");
        }

        if (!File.Exists(expectedBackupPath))
        {
            throw new FileNotFoundException("Display preferences backup is unavailable.", expectedBackupPath);
        }

        _ = LoadSafeDocument(expectedBackupPath);
        if (!ComputeSha256(expectedBackupPath).Equals(
                snapshot.OriginalSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O backup de exibição não corresponde ao snapshot original.");
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
                "Display settings changed after optimization; rollback refused to overwrite newer user edits.");
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            $".{Path.GetFileName(settingsPath)}.{context.TransactionId:N}.display-rollback.tmp");
        var displacedPath = Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            $".{Path.GetFileName(settingsPath)}.{context.TransactionId:N}.display-rollback-current.bak");
        if (File.Exists(temporaryPath) || File.Exists(displacedPath))
        {
            throw new IOException("Um artefato de rollback de exibição já existe.");
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
                    "As configurações de exibição mudaram durante o rollback; a restauração foi cancelada.");
            }

            ReplaceAndVerifyDisplacedOriginal(
                temporaryPath,
                settingsPath,
                displacedPath,
                currentHash,
                "As configurações de exibição mudaram no instante do rollback; a versão mais recente foi restaurada.");

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

    /// <summary>
    /// GTA V/FiveM settings files are not consistent about boolean
    /// representation: some values use "true"/"false", others use "0"/"1".
    /// Accept both when reading, never guess a third format.
    /// </summary>
    private static bool TryParseFlexibleBoolean(string raw, out bool value)
    {
        if (bool.TryParse(raw, out value))
        {
            return true;
        }

        if (raw == "0")
        {
            value = false;
            return true;
        }

        if (raw == "1")
        {
            value = true;
            return true;
        }

        value = false;
        return false;
    }

    private static string FormatFlexibleBoolean(bool value, string existingRawValue)
    {
        return existingRawValue is "0" or "1"
            ? (value ? "1" : "0")
            : (value ? "true" : "false");
    }

    private bool IsTargetRunning()
    {
        return target == GraphicsSettingsTarget.FiveM
            ? processInspector.IsRunningFrom(gameRoot!)
            : gtaVProcessInspector.IsRunningFrom(gameRoot);
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
            throw new InvalidDataException("O arquivo de exibição excede o limite seguro de 4 MB.");
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
}
