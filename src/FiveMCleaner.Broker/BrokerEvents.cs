namespace FiveMCleaner.Broker;

internal enum BrokerEventKind
{
    Started,
    Progress,
    Completed,
    RollbackStarted,
    RollbackCompleted,
    Rejected,
    Failed
}

internal sealed record BrokerEvent
{
    public required int SchemaVersion { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required BrokerEventKind Kind { get; init; }

    public Guid? TransactionId { get; init; }

    public string? ActionId { get; init; }

    public required string Message { get; init; }

    public int? CompletedWeight { get; init; }

    public int? TotalWeight { get; init; }

    public bool? Success { get; init; }

    public string? State { get; init; }

    public string? ErrorCode { get; init; }

    public IReadOnlyList<string>? AppliedActionIds { get; init; }
}

internal static class BrokerEventSchema
{
    public const int CurrentVersion = 1;
}
