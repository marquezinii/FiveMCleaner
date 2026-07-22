using System.Text.Json;
using System.Text.Json.Serialization;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.Windows.Engine;

public enum WindowsTransactionState
{
    Created,
    Applying,
    Committing,
    Committed,
    CommittedWithErrors,
    AwaitingElevation,
    AwaitingElevationRollback,
    AwaitingStandardRollback,
    RollingBack,
    RolledBack,
    Failed,
    RollbackFailed
}

public enum WindowsActionJournalState
{
    Pending,
    SkippedPrivilege,
    DeferredPrivilege,
    Applying,
    Applied,
    Committing,
    Committed,
    Skipped,
    RollingBack,
    RolledBack,
    Failed,
    RollbackFailed
}

public sealed record WindowsActionJournalEntry
{
    public required int Sequence { get; init; }

    public required string ActionId { get; init; }

    public required int Version { get; init; }

    public required RequiredPrivilege RequiredPrivilege { get; init; }

    public required ActionReversibility Reversibility { get; init; }

    public required WindowsActionJournalState State { get; set; }

    /// <summary>
    /// Semantic outcome for reporting. Independent from <see cref="State"/>,
    /// which drives the transactional machine and rollback eligibility.
    /// </summary>
    public ActionExecutionOutcome Outcome { get; set; } = ActionExecutionOutcome.Pending;

    /// <summary>Reason an action was skipped or not run, for the report.</summary>
    public string? OutcomeReason { get; set; }

    public bool Changed { get; set; }

    public string? SnapshotJson { get; set; }

    public List<string> Messages { get; init; } = [];

    public string? Error { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed record WindowsTransactionJournal
{
    public required Guid TransactionId { get; init; }

    public required int SchemaVersion { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; set; }

    public required bool WasElevated { get; set; }

    public required WindowsTransactionState State { get; set; }

    public string? Error { get; set; }

    public List<WindowsActionJournalEntry> Actions { get; init; } = [];
}

public interface IWindowsTransactionJournalStore
{
    Task SaveAsync(WindowsTransactionJournal journal, CancellationToken cancellationToken);

    Task<WindowsTransactionJournal?> LoadAsync(Guid transactionId, CancellationToken cancellationToken);
}

public sealed class JsonWindowsTransactionJournalStore : IWindowsTransactionJournalStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string rootDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions serializerOptions;

    public JsonWindowsTransactionJournalStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        serializerOptions = new JsonSerializerOptions(FiveMCleanerJson.Options)
        {
            WriteIndented = true
        };
        serializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public static JsonWindowsTransactionJournalStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "FiveMCleaner", "Transactions");
        return new JsonWindowsTransactionJournalStore(path);
    }

    public async Task SaveAsync(
        WindowsTransactionJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (journal.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported journal schema {journal.SchemaVersion}.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(rootDirectory);
            EnsureDirectoryIsNotReparsePoint(rootDirectory);

            journal.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var destination = GetPath(journal.TransactionId);
            var temporary = Path.Combine(
                rootDirectory,
                $".{journal.TransactionId:N}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        journal,
                        serializerOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<WindowsTransactionJournal?> LoadAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetPath(transactionId);
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var journal = await JsonSerializer.DeserializeAsync<WindowsTransactionJournal>(
                stream,
                serializerOptions,
                cancellationToken).ConfigureAwait(false);

            if (journal is null)
            {
                throw new JsonException($"Journal '{path}' was empty.");
            }

            if (journal.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported journal schema {journal.SchemaVersion}.");
            }

            if (journal.TransactionId != transactionId)
            {
                throw new JsonException("The journal transaction ID does not match its file name.");
            }

            return journal;
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPath(Guid transactionId)
    {
        return Path.Combine(rootDirectory, $"{transactionId:N}.json");
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        var directory = new DirectoryInfo(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Journal directory '{path}' cannot be a reparse point.");
        }
    }
}
