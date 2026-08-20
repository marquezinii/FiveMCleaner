using System.Xml.Linq;
using FiveMCleaner.Windows.Infrastructure;

namespace FiveMCleaner.Windows.Actions;

internal sealed record SafeXmlSettingsSnapshot(
    string SettingsPath,
    string BackupPath,
    string OriginalSha256,
    string AppliedSha256,
    IReadOnlyList<string> ChangedSettings);

internal sealed record SafeXmlTransactionMessages(
    string BackupDirectoryIsReparsePoint,
    string ApplyArtifactExists,
    string FileTooLarge,
    string SourceChangedDuringApply,
    string SourceChangedAtApplySwap,
    string SnapshotPathsInvalid,
    string BackupUnavailable,
    string BackupHashInvalid,
    string NewerEditsPreventRollback,
    string RollbackArtifactExists,
    string SourceChangedDuringRollback,
    string SourceChangedAtRollbackSwap);

internal sealed class SafeXmlSettingsTransaction
{
    private readonly string artifactToken;
    private readonly Func<bool> isTargetRunning;
    private readonly SafeXmlTransactionMessages messages;
    private readonly string settingsPath;

    public SafeXmlSettingsTransaction(
        string settingsPath,
        string artifactToken,
        SafeXmlTransactionMessages messages,
        Func<bool> isTargetRunning)
    {
        this.settingsPath = Path.GetFullPath(settingsPath);
        this.artifactToken = artifactToken;
        this.messages = messages ?? throw new ArgumentNullException(nameof(messages));
        this.isTargetRunning = isTargetRunning ?? throw new ArgumentNullException(nameof(isTargetRunning));
    }

    public SafeXmlSettingsSnapshot Apply(
        XDocument document,
        Guid transactionId,
        string originalHash,
        IReadOnlyList<string> changedSettings)
    {
        var artifacts = CreateArtifacts(transactionId);
        EnsureBackupDirectory(artifacts.BackupPath);
        EnsureArtifactsDoNotExist(
            messages.ApplyArtifactExists,
            artifacts.TemporaryPath,
            artifacts.BackupPath);

        try
        {
            SafeXmlDocumentStore.SaveDocument(document, artifacts.TemporaryPath);
            _ = SafeXmlDocumentStore.LoadSafeDocument(
                artifacts.TemporaryPath,
                messages.FileTooLarge);
            var appliedHash = SafeXmlDocumentStore.ComputeSha256(artifacts.TemporaryPath);
            if (isTargetRunning())
            {
                throw new IOException(
                    "O jogo foi iniciado durante a preparação; nenhuma configuração foi substituída.");
            }

            if (!SafeXmlDocumentStore.ComputeSha256(settingsPath).Equals(
                    originalHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(messages.SourceChangedDuringApply);
            }

            SafeXmlDocumentStore.ReplaceAndVerifyDisplacedOriginal(
                artifacts.TemporaryPath,
                settingsPath,
                artifacts.BackupPath,
                originalHash,
                messages.SourceChangedAtApplySwap);

            return new SafeXmlSettingsSnapshot(
                settingsPath,
                artifacts.BackupPath,
                originalHash,
                appliedHash,
                changedSettings);
        }
        finally
        {
            DeleteIfExists(artifacts.TemporaryPath);
        }
    }

    public void Rollback(Guid transactionId, SafeXmlSettingsSnapshot snapshot)
    {
        var artifacts = CreateArtifacts(transactionId);
        if (!Path.GetFullPath(snapshot.SettingsPath).Equals(
                settingsPath,
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(snapshot.BackupPath).Equals(
                artifacts.BackupPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(messages.SnapshotPathsInvalid);
        }

        if (!File.Exists(artifacts.BackupPath))
        {
            throw new FileNotFoundException(messages.BackupUnavailable, artifacts.BackupPath);
        }

        _ = SafeXmlDocumentStore.LoadSafeDocument(artifacts.BackupPath, messages.FileTooLarge);
        if (!SafeXmlDocumentStore.ComputeSha256(artifacts.BackupPath).Equals(
                snapshot.OriginalSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(messages.BackupHashInvalid);
        }

        if (!File.Exists(settingsPath))
        {
            if (isTargetRunning())
            {
                throw new IOException(
                    "O jogo foi iniciado durante o rollback; nenhuma configuração foi restaurada.");
            }

            File.Copy(artifacts.BackupPath, settingsPath, overwrite: false);
            return;
        }

        var currentHash = SafeXmlDocumentStore.ComputeSha256(settingsPath);
        if (!currentHash.Equals(snapshot.AppliedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(messages.NewerEditsPreventRollback);
        }

        EnsureArtifactsDoNotExist(
            messages.RollbackArtifactExists,
            artifacts.RollbackTemporaryPath,
            artifacts.RollbackDisplacedPath);
        File.Copy(artifacts.BackupPath, artifacts.RollbackTemporaryPath, overwrite: false);
        try
        {
            _ = SafeXmlDocumentStore.LoadSafeDocument(
                artifacts.RollbackTemporaryPath,
                messages.FileTooLarge);
            if (isTargetRunning())
            {
                throw new IOException(
                    "O jogo foi iniciado durante o rollback; nenhuma configuração foi substituída.");
            }

            if (!SafeXmlDocumentStore.ComputeSha256(settingsPath).Equals(
                    currentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(messages.SourceChangedDuringRollback);
            }

            SafeXmlDocumentStore.ReplaceAndVerifyDisplacedOriginal(
                artifacts.RollbackTemporaryPath,
                settingsPath,
                artifacts.RollbackDisplacedPath,
                currentHash,
                messages.SourceChangedAtRollbackSwap);
            File.Delete(artifacts.RollbackDisplacedPath);
        }
        finally
        {
            DeleteIfExists(artifacts.RollbackTemporaryPath);
        }
    }

    private XmlTransactionArtifacts CreateArtifacts(Guid transactionId)
    {
        var directory = Path.GetDirectoryName(settingsPath)!;
        var fileName = Path.GetFileName(settingsPath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(settingsPath);
        var token = string.IsNullOrEmpty(artifactToken) ? string.Empty : $".{artifactToken}";
        var rollbackToken = string.IsNullOrEmpty(artifactToken)
            ? ".rollback"
            : $".{artifactToken}-rollback";
        return new XmlTransactionArtifacts(
            Path.Combine(directory, $".{fileName}.{transactionId:N}{token}.tmp"),
            Path.Combine(
                directory,
                ".fivemcleaner-backups",
                $"{fileNameWithoutExtension}.{transactionId:N}{token}.bak"),
            Path.Combine(directory, $".{fileName}.{transactionId:N}{rollbackToken}.tmp"),
            Path.Combine(
                directory,
                $".{fileName}.{transactionId:N}{rollbackToken}-current.bak"));
    }

    private void EnsureBackupDirectory(string backupPath)
    {
        var backupDirectory = Path.GetDirectoryName(backupPath)!;
        Directory.CreateDirectory(backupDirectory);
        if ((new DirectoryInfo(backupDirectory).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(messages.BackupDirectoryIsReparsePoint);
        }
    }

    private static void EnsureArtifactsDoNotExist(string message, params string[] paths)
    {
        if (paths.Any(File.Exists))
        {
            throw new IOException(message);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record XmlTransactionArtifacts(
        string TemporaryPath,
        string BackupPath,
        string RollbackTemporaryPath,
        string RollbackDisplacedPath);
}
