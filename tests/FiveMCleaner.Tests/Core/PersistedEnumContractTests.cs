using System.Text.Json;
using System.Text.Json.Nodes;
using FiveMCleaner.Contracts;
using FiveMCleaner.Core.Catalog;
using FiveMCleaner.Windows.Engine;
using Xunit;

namespace FiveMCleaner.Tests.Core;

/// <summary>
/// Freezes the durable vocabulary written to
/// <c>%LOCALAPPDATA%\FiveMCleaner\Transactions\{id}.json</c>. Journals survive
/// across application versions and are the only source that makes a past
/// transaction auditable and reversible, so a renamed, removed or reordered
/// member silently destroys rollback for already-installed users. These tests
/// exist to make that class of change impossible to merge unnoticed.
/// </summary>
public sealed class PersistedEnumContractTests
{
    [Fact]
    public void ActionExecutionOutcome_MembersAndValuesAreFrozen()
    {
        AssertMembersAreFrozen<ActionExecutionOutcome>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Pending"] = 0,
            ["Verified"] = 1,
            ["Applied"] = 2,
            ["Skipped"] = 3,
            ["Warning"] = 4,
            ["Failed"] = 5,
            ["RolledBack"] = 6,
            ["RollbackFailed"] = 7,
            ["NotRun"] = 8
        });
    }

    [Fact]
    public void ActionExecutionOutcome_SerializesAsCamelCaseStrings()
    {
        AssertSerializesAs(ActionExecutionOutcome.Pending, "pending");
        AssertSerializesAs(ActionExecutionOutcome.Verified, "verified");
        AssertSerializesAs(ActionExecutionOutcome.Applied, "applied");
        AssertSerializesAs(ActionExecutionOutcome.Skipped, "skipped");
        AssertSerializesAs(ActionExecutionOutcome.Warning, "warning");
        AssertSerializesAs(ActionExecutionOutcome.Failed, "failed");
        AssertSerializesAs(ActionExecutionOutcome.RolledBack, "rolledBack");
        AssertSerializesAs(ActionExecutionOutcome.RollbackFailed, "rollbackFailed");
        AssertSerializesAs(ActionExecutionOutcome.NotRun, "notRun");
    }

    [Fact]
    public void TransactionState_MembersAndValuesAreFrozen()
    {
        AssertMembersAreFrozen<TransactionState>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Created"] = 0,
            ["Applying"] = 1,
            ["Committing"] = 2,
            ["Committed"] = 3,
            ["CommittedWithErrors"] = 4,
            ["AwaitingElevation"] = 5,
            ["AwaitingElevationRollback"] = 6,
            ["AwaitingStandardRollback"] = 7,
            ["RollingBack"] = 8,
            ["RolledBack"] = 9,
            ["Failed"] = 10,
            ["RollbackFailed"] = 11
        });
    }

    [Fact]
    public void TransactionState_SerializesAsCamelCaseStrings()
    {
        AssertSerializesAs(TransactionState.Created, "created");
        AssertSerializesAs(TransactionState.Applying, "applying");
        AssertSerializesAs(TransactionState.Committing, "committing");
        AssertSerializesAs(TransactionState.Committed, "committed");
        AssertSerializesAs(TransactionState.CommittedWithErrors, "committedWithErrors");
        AssertSerializesAs(TransactionState.AwaitingElevation, "awaitingElevation");
        AssertSerializesAs(TransactionState.AwaitingElevationRollback, "awaitingElevationRollback");
        AssertSerializesAs(TransactionState.AwaitingStandardRollback, "awaitingStandardRollback");
        AssertSerializesAs(TransactionState.RollingBack, "rollingBack");
        AssertSerializesAs(TransactionState.RolledBack, "rolledBack");
        AssertSerializesAs(TransactionState.Failed, "failed");
        AssertSerializesAs(TransactionState.RollbackFailed, "rollbackFailed");
    }

    [Fact]
    public void ActionJournalState_MembersAndValuesAreFrozen()
    {
        AssertMembersAreFrozen<ActionJournalState>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Pending"] = 0,
            ["SkippedPrivilege"] = 1,
            ["DeferredPrivilege"] = 2,
            ["Applying"] = 3,
            ["Applied"] = 4,
            ["Committing"] = 5,
            ["Committed"] = 6,
            ["Skipped"] = 7,
            ["RollingBack"] = 8,
            ["RolledBack"] = 9,
            ["Failed"] = 10,
            ["RollbackFailed"] = 11
        });
    }

    [Fact]
    public void ActionJournalState_SerializesAsCamelCaseStrings()
    {
        AssertSerializesAs(ActionJournalState.Pending, "pending");
        AssertSerializesAs(ActionJournalState.SkippedPrivilege, "skippedPrivilege");
        AssertSerializesAs(ActionJournalState.DeferredPrivilege, "deferredPrivilege");
        AssertSerializesAs(ActionJournalState.Applying, "applying");
        AssertSerializesAs(ActionJournalState.Applied, "applied");
        AssertSerializesAs(ActionJournalState.Committing, "committing");
        AssertSerializesAs(ActionJournalState.Committed, "committed");
        AssertSerializesAs(ActionJournalState.Skipped, "skipped");
        AssertSerializesAs(ActionJournalState.RollingBack, "rollingBack");
        AssertSerializesAs(ActionJournalState.RolledBack, "rolledBack");
        AssertSerializesAs(ActionJournalState.Failed, "failed");
        AssertSerializesAs(ActionJournalState.RollbackFailed, "rollbackFailed");
    }

    [Fact]
    public void ActionReversibility_MembersAndValuesAreFrozen()
    {
        AssertMembersAreFrozen<ActionReversibility>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ReadOnly"] = 0,
            ["FullyReversible"] = 1,
            ["RebuildableData"] = 2,
            ["Irreversible"] = 3
        });
    }

    [Fact]
    public void ActionReversibility_SerializesAsCamelCaseStrings()
    {
        AssertSerializesAs(ActionReversibility.ReadOnly, "readOnly");
        AssertSerializesAs(ActionReversibility.FullyReversible, "fullyReversible");
        AssertSerializesAs(ActionReversibility.RebuildableData, "rebuildableData");
        AssertSerializesAs(ActionReversibility.Irreversible, "irreversible");
    }

    [Fact]
    public void RequiredPrivilege_MembersAndValuesAreFrozen()
    {
        AssertMembersAreFrozen<RequiredPrivilege>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["StandardUser"] = 0,
            ["Administrator"] = 1
        });
    }

    [Fact]
    public void RequiredPrivilege_SerializesAsCamelCaseStrings()
    {
        AssertSerializesAs(RequiredPrivilege.StandardUser, "standardUser");
        AssertSerializesAs(RequiredPrivilege.Administrator, "administrator");
    }

    [Fact]
    public async Task Journal_WritesEnumsToDiskAsCamelCaseStrings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var store = new JsonWindowsTransactionJournalStore(directory);
            var journal = CreateJournal();

            await store.SaveAsync(journal, cancellationToken);

            var file = Assert.Single(Directory.GetFiles(directory, "*.json"));
            var root = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))!.AsObject();

            Assert.Equal("committedWithErrors", root["state"]!.GetValue<string>());

            var entry = root["actions"]!.AsArray()[0]!.AsObject();
            Assert.Equal("rollbackFailed", entry["state"]!.GetValue<string>());
            Assert.Equal("rolledBack", entry["outcome"]!.GetValue<string>());
            Assert.Equal("administrator", entry["requiredPrivilege"]!.GetValue<string>());
            Assert.Equal("fullyReversible", entry["reversibility"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Journal_WrittenBeforeOutcomesExisted_StillLoads()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var store = new JsonWindowsTransactionJournalStore(directory);
            var journal = CreateJournal();
            await store.SaveAsync(journal, cancellationToken);

            // Reproduces a journal produced by a version that did not record
            // outcomes yet: rollback of those transactions must keep working.
            var file = Assert.Single(Directory.GetFiles(directory, "*.json"));
            var root = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))!.AsObject();
            root["actions"]!.AsArray()[0]!.AsObject().Remove("outcome");
            await File.WriteAllTextAsync(file, root.ToJsonString(), cancellationToken);

            var restored = await store.LoadAsync(journal.TransactionId, cancellationToken);

            Assert.NotNull(restored);
            Assert.Equal(TransactionState.CommittedWithErrors, restored.State);
            Assert.Equal(ActionExecutionOutcome.Pending, restored.Actions[0].Outcome);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WindowsTransactionJournal CreateJournal()
    {
        return new WindowsTransactionJournal
        {
            TransactionId = Guid.NewGuid(),
            SchemaVersion = 1,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            WasElevated = true,
            State = TransactionState.CommittedWithErrors,
            Actions =
            [
                new WindowsActionJournalEntry
                {
                    Sequence = 1,
                    ActionId = OptimizationActionIds.EnableGameMode,
                    Version = 1,
                    RequiredPrivilege = RequiredPrivilege.Administrator,
                    Reversibility = ActionReversibility.FullyReversible,
                    State = ActionJournalState.RollbackFailed,
                    Outcome = ActionExecutionOutcome.RolledBack
                }
            ]
        };
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "FiveMCleanerJournalContract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void AssertMembersAreFrozen<TEnum>(IReadOnlyDictionary<string, int> expected)
        where TEnum : struct, Enum
    {
        var actual = Enum.GetValues<TEnum>()
            .ToDictionary(
                value => value.ToString(),
                value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(pair => pair.Value), actual.OrderBy(pair => pair.Value));
    }

    private static void AssertSerializesAs<TEnum>(TEnum value, string expected)
        where TEnum : struct, Enum
    {
        Assert.Equal(
            $"\"{expected}\"",
            JsonSerializer.Serialize(value, FiveMCleanerJson.Options));
    }
}
