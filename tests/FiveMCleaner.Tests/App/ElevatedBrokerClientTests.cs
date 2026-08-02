using System.IO;
using System.Text.Json;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;
using Xunit;
using BrokerEventKindWire = FiveMCleaner.App.Services.ElevatedBrokerClient.BrokerEventKindWire;
using BrokerEventWire = FiveMCleaner.App.Services.ElevatedBrokerClient.BrokerEventWire;

namespace FiveMCleaner.Tests.App;

public sealed class ElevatedBrokerClientTerminalReadTests
{
    private static readonly Guid TransactionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly IProgress<AppProgressUpdate> NoProgress = new Progress<AppProgressUpdate>();
    private static readonly ILocalizationService Localization = new LocalizationService(
        System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    private static BrokerEventWire Event(long sequence, BrokerEventKindWire kind, string message, Guid? transactionId = null, bool? success = null) =>
        new()
        {
            SchemaVersion = 1,
            Sequence = sequence,
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = kind,
            TransactionId = transactionId ?? TransactionId,
            Message = message,
            Success = success
        };

    private static StringReader Reader(params BrokerEventWire[] events)
    {
        var lines = events.Select(item => JsonSerializer.Serialize(item, FiveMCleanerJson.Options));
        return new StringReader(string.Join('\n', lines));
    }

    // Regression: the old loop kept reading past the terminal event up to the
    // 128-event cap, so a plan with enough progress events pushed the terminal
    // out of the window and a successful elevated phase was reported failed.
    [Fact]
    public async Task ReadUntilTerminalAsync_StopsReadingAtTheTerminalEvent()
    {
        var events = new[]
        {
            Event(1, BrokerEventKindWire.Started, "started"),
            Event(2, BrokerEventKindWire.Completed, "done", success: true),
            // If the reader continued past the terminal it would parse this
            // line (schemaVersion 9) and throw.
            new BrokerEventWire
            {
                SchemaVersion = 9,
                Sequence = 3,
                TimestampUtc = DateTimeOffset.UtcNow,
                Kind = BrokerEventKindWire.Completed,
                TransactionId = TransactionId,
                Message = "must not be read"
            }
        };

        var terminal = await ElevatedBrokerClient.ReadUntilTerminalAsync(
            Reader(events), TransactionId, NoProgress, Localization, CancellationToken.None);

        Assert.NotNull(terminal);
        Assert.Equal(BrokerEventKindWire.Completed, terminal.Kind);
        Assert.True(terminal.Success);
    }

    [Fact]
    public async Task ReadUntilTerminalAsync_TreatsRejectedAndFailedAsTerminal()
    {
        foreach (var kind in new[] { BrokerEventKindWire.Rejected, BrokerEventKindWire.Failed })
        {
            var terminal = await ElevatedBrokerClient.ReadUntilTerminalAsync(
                Reader(Event(1, kind, "stopped", success: false)),
                TransactionId,
                NoProgress,
                Localization,
                CancellationToken.None);

            Assert.Equal(kind, terminal!.Kind);
            Assert.False(terminal.Success);
        }
    }

    [Fact]
    public async Task ReadUntilTerminalAsync_EventCapExceededWithoutTerminal_Throws()
    {
        var events = Enumerable.Range(1, 200)
            .Select(i => Event(i, BrokerEventKindWire.Progress, $"p{i}"))
            .ToArray();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ElevatedBrokerClient.ReadUntilTerminalAsync(
                Reader(events), TransactionId, NoProgress, Localization, CancellationToken.None));
    }

    [Fact]
    public async Task ReadUntilTerminalAsync_ReachingEofWithoutTerminal_ReturnsNull()
    {
        var events = new[]
        {
            Event(1, BrokerEventKindWire.Progress, "p1"),
            Event(2, BrokerEventKindWire.Progress, "p2")
        };

        var terminal = await ElevatedBrokerClient.ReadUntilTerminalAsync(
            Reader(events), TransactionId, NoProgress, Localization, CancellationToken.None);

        Assert.Null(terminal);
    }

    [Fact]
    public async Task ReadUntilTerminalAsync_OutOfOrderSequence_Throws()
    {
        var events = new[]
        {
            Event(1, BrokerEventKindWire.Progress, "p1"),
            Event(1, BrokerEventKindWire.Progress, "p1-duplicate")
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ElevatedBrokerClient.ReadUntilTerminalAsync(
                Reader(events), TransactionId, NoProgress, Localization, CancellationToken.None));
    }

    [Fact]
    public async Task ReadUntilTerminalAsync_TerminalConfirmingAnotherTransaction_Throws()
    {
        var events = new[]
        {
            Event(1, BrokerEventKindWire.Completed, "wrong-tx", transactionId: Guid.NewGuid(), success: true)
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ElevatedBrokerClient.ReadUntilTerminalAsync(
                Reader(events), TransactionId, NoProgress, Localization, CancellationToken.None));
    }

    [Fact]
    public async Task ReadUntilTerminalAsync_ReportsLocalizedBrokerPhaseHeadlines()
    {
        var captured = new List<AppProgressUpdate>();
        var progress = new Progress<AppProgressUpdate>(captured.Add);
        var events = new[]
        {
            Event(1, BrokerEventKindWire.Progress, "applying something"),
            Event(2, BrokerEventKindWire.RollbackStarted, "restoring something"),
            Event(3, BrokerEventKindWire.Completed, "done", success: true)
        };

        await ElevatedBrokerClient.ReadUntilTerminalAsync(
            Reader(events), TransactionId, progress, Localization, CancellationToken.None);

        var progressUpdate = captured[0];
        var rollbackUpdate = captured[1];
        Assert.Equal("Applying administrative adjustments", progressUpdate.Headline);
        Assert.Equal("Restoring administrative settings", rollbackUpdate.Headline);
    }
}
