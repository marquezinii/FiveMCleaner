using System.Xml.Linq;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

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
    private static readonly SafeXmlTransactionMessages TransactionMessages = new(
        "Display preferences backup directory cannot be a reparse point.",
        "A display preferences transaction artifact already exists.",
        "O arquivo de exibição excede o limite seguro de 4 MB.",
        "As configurações de exibição mudaram durante a preparação; a gravação foi cancelada.",
        "As configurações de exibição mudaram no instante da troca; a versão mais recente foi restaurada.",
        "O snapshot de exibição aponta para caminhos inesperados.",
        "Display preferences backup is unavailable.",
        "O backup de exibição não corresponde ao snapshot original.",
        "Display settings changed after optimization; rollback refused to overwrite newer user edits.",
        "Um artefato de rollback de exibição já existe.",
        "As configurações de exibição mudaram durante o rollback; a restauração foi cancelada.",
        "As configurações de exibição mudaram no instante do rollback; a versão mais recente foi restaurada.");

    private readonly string settingsPath;
    private readonly string? gameRoot;
    private readonly IReadOnlyDictionary<string, bool> preferences;
    private readonly GraphicsTargetProcessGuard processGuard;
    private readonly SafeXmlSettingsTransaction transaction;
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
        processGuard = new GraphicsTargetProcessGuard(
            target,
            this.gameRoot,
            processInspector,
            gtaVProcessInspector);
        transaction = new SafeXmlSettingsTransaction(
            this.settingsPath,
            "display",
            TransactionMessages,
            processGuard.IsRunning);
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

        processGuard.EnsureStopped(
            "FiveM precisa estar fechado para editar a exibição.",
            "GTA V precisa estar fechado para editar a exibição.");

        var (document, originalHash) = SafeXmlDocumentStore.LoadSafeDocumentWithHash(
            settingsPath,
            "O arquivo de exibição excede o limite seguro de 4 MB.");
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

        var snapshot = transaction.Apply(document, context.TransactionId, originalHash, changed);
        return Task.FromResult(WindowsActionApplyResult.ChangedWith(
            snapshot,
            $"Backup criado e {changed.Count} preferência(s) de exibição atualizada(s)."));
    }

    public override Task RollbackAsync(
        WindowsActionContext context,
        string? snapshotJson,
        CancellationToken cancellationToken)
    {
        var snapshot = WindowsActionSnapshot.Deserialize<SafeXmlSettingsSnapshot>(snapshotJson);
        cancellationToken.ThrowIfCancellationRequested();
        processGuard.EnsureStopped(
            "FiveM precisa estar fechado para restaurar a exibição.",
            "GTA V precisa estar fechado para restaurar a exibição.");
        transaction.Rollback(context.TransactionId, snapshot);
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

}
