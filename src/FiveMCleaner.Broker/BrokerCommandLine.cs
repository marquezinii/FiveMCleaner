namespace FiveMCleaner.Broker;

internal enum BrokerOperation
{
    ExecutePlan,
    Rollback
}

internal sealed record BrokerCommand(
    BrokerOperation Operation,
    Guid PipeId,
    string? RequestPath,
    Guid? RollbackTransactionId);

internal sealed class BrokerUsageException : Exception
{
    public BrokerUsageException(string message)
        : base(message)
    {
    }
}

internal static class BrokerCommandLine
{
    private const string RequestOption = "--request";
    private const string PipeOption = "--pipe";
    private const string RollbackOption = "--rollback";

    public static BrokerCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 4)
        {
            throw new BrokerUsageException(
                "Expected --request <file> --pipe <guid> or --rollback <guid> --pipe <guid>.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var option = args[index];
            var value = args[index + 1];
            if (option is not (RequestOption or PipeOption or RollbackOption)
                || string.IsNullOrWhiteSpace(value)
                || !values.TryAdd(option, value))
            {
                throw new BrokerUsageException("The broker command line is invalid.");
            }
        }

        if (!values.TryGetValue(PipeOption, out var pipeValue)
            || !Guid.TryParse(pipeValue, out var pipeId)
            || pipeId == Guid.Empty)
        {
            throw new BrokerUsageException("--pipe must contain a non-empty GUID.");
        }

        var hasRequest = values.TryGetValue(RequestOption, out var requestPath);
        var hasRollback = values.TryGetValue(RollbackOption, out var rollbackValue);
        if (hasRequest == hasRollback)
        {
            throw new BrokerUsageException("Choose exactly one broker operation.");
        }

        if (hasRequest)
        {
            return new BrokerCommand(
                BrokerOperation.ExecutePlan,
                pipeId,
                requestPath,
                RollbackTransactionId: null);
        }

        if (!Guid.TryParse(rollbackValue, out var transactionId) || transactionId == Guid.Empty)
        {
            throw new BrokerUsageException("--rollback must contain a non-empty transaction GUID.");
        }

        return new BrokerCommand(
            BrokerOperation.Rollback,
            pipeId,
            RequestPath: null,
            transactionId);
    }
}
