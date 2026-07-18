using FiveMCleaner.Windows.Engine;

namespace FiveMCleaner.Broker;

internal static class BrokerExitCode
{
    public const int Success = 0;
    public const int InvalidArguments = 2;
    public const int PipeConnectionFailed = 3;
    public const int RequestRejected = 4;
    public const int ExecutionFailed = 5;
    public const int RollbackFailed = 6;
    public const int NotElevated = 7;
}

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        BrokerCommand command;
        try
        {
            command = BrokerCommandLine.Parse(args);
        }
        catch (BrokerUsageException)
        {
            return BrokerExitCode.InvalidArguments;
        }

        NamedPipeEventWriter events;
        try
        {
            events = await NamedPipeEventWriter.ConnectAsync(
                command.PipeId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (BrokerPipeException)
        {
            return BrokerExitCode.PipeConnectionFailed;
        }

        await using (events.ConfigureAwait(false))
        {
            try
            {
                ElevationGuard.EnsureElevated();
                return command.Operation switch
                {
                    BrokerOperation.ExecutePlan => await ExecutePlanAsync(command, events)
                        .ConfigureAwait(false),
                    BrokerOperation.Rollback => await RollbackAsync(command, events)
                        .ConfigureAwait(false),
                    _ => BrokerExitCode.InvalidArguments
                };
            }
            catch (BrokerRequestException exception)
            {
                return PublishFailure(
                    events,
                    BrokerEventKind.Rejected,
                    exception.Message,
                    exception.ErrorCode,
                    BrokerExitCode.RequestRejected);
            }
            catch (BrokerNotElevatedException)
            {
                return PublishFailure(
                    events,
                    BrokerEventKind.Failed,
                    "O broker não recebeu um token administrativo válido.",
                    "broker-not-elevated",
                    BrokerExitCode.NotElevated);
            }
            catch (BrokerPipeException)
            {
                return BrokerExitCode.PipeConnectionFailed;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException
                or StackOverflowException
                or AccessViolationException))
            {
                var exitCode = command.Operation == BrokerOperation.Rollback
                    ? BrokerExitCode.RollbackFailed
                    : BrokerExitCode.ExecutionFailed;
                return PublishFailure(
                    events,
                    BrokerEventKind.Failed,
                    "A operação elevada falhou com segurança. Consulte o journal local.",
                    "broker-operation-failed",
                    exitCode);
            }
        }
    }

    private static async Task<int> ExecutePlanAsync(
        BrokerCommand command,
        NamedPipeEventWriter events)
    {
        var plan = await new PlanRequestFileLoader().LoadAsync(
            command.RequestPath!,
            CancellationToken.None).ConfigureAwait(false);
        var validated = new PlanValidator().Validate(plan);

        events.Publish(
            BrokerEventKind.Started,
            "Plano administrativo validado. Iniciando transação elevada.",
            item => item.TransactionId = plan.PlanId);

        var runtime = WindowsAdministratorRuntimeAdapter.CreateDefault();
        var result = await runtime.ExecuteAsync(
            validated,
            events,
            CancellationToken.None).ConfigureAwait(false);

        if (result.State != WindowsTransactionState.Committed)
        {
            events.Publish(
                BrokerEventKind.Failed,
                "A transação não foi confirmada; alterações aplicadas foram revertidas quando possível.",
                item =>
                {
                    item.TransactionId = result.TransactionId;
                    item.Success = false;
                    item.State = result.State.ToString();
                    item.ErrorCode = "transaction-not-committed";
                    item.AppliedActionIds = result.AppliedActionIds;
                });
            return BrokerExitCode.ExecutionFailed;
        }

        events.Publish(
            BrokerEventKind.Completed,
            "Alterações administrativas concluídas e registradas.",
            item =>
            {
                item.TransactionId = result.TransactionId;
                item.Success = true;
                item.State = result.State.ToString();
                item.AppliedActionIds = result.AppliedActionIds;
            });
        return BrokerExitCode.Success;
    }

    private static async Task<int> RollbackAsync(
        BrokerCommand command,
        NamedPipeEventWriter events)
    {
        var transactionId = command.RollbackTransactionId!.Value;
        events.Publish(
            BrokerEventKind.RollbackStarted,
            "Iniciando reversão da transação elevada.",
            item => item.TransactionId = transactionId);

        var runtime = WindowsAdministratorRuntimeAdapter.CreateDefault();
        var result = await runtime.RollbackAsync(
            transactionId,
            CancellationToken.None).ConfigureAwait(false);

        if (result.State != WindowsTransactionState.RolledBack)
        {
            events.Publish(
                BrokerEventKind.Failed,
                "A reversão não foi concluída. O journal preserva o estado para diagnóstico.",
                item =>
                {
                    item.TransactionId = transactionId;
                    item.Success = false;
                    item.State = result.State.ToString();
                    item.ErrorCode = "rollback-not-completed";
                });
            return BrokerExitCode.RollbackFailed;
        }

        events.Publish(
            BrokerEventKind.RollbackCompleted,
            "Reversão concluída.",
            item =>
            {
                item.TransactionId = transactionId;
                item.Success = true;
                item.State = result.State.ToString();
                item.AppliedActionIds = result.AppliedActionIds;
            });
        return BrokerExitCode.Success;
    }

    private static int PublishFailure(
        NamedPipeEventWriter events,
        BrokerEventKind kind,
        string message,
        string errorCode,
        int exitCode)
    {
        try
        {
            events.Publish(
                kind,
                message,
                item =>
                {
                    item.Success = false;
                    item.ErrorCode = errorCode;
                });
            return exitCode;
        }
        catch (BrokerPipeException)
        {
            return BrokerExitCode.PipeConnectionFailed;
        }
    }
}
