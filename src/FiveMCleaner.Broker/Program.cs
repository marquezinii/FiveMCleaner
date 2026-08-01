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
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(90);

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        BrokerDiagnosticsLog.Record("broker-started");
        BrokerCommand command;
        try
        {
            command = BrokerCommandLine.Parse(args);
        }
        catch (BrokerUsageException)
        {
            BrokerDiagnosticsLog.Record("invalid-arguments");
            return BrokerExitCode.InvalidArguments;
        }

        NamedPipeEventWriter events;
        try
        {
            events = await NamedPipeEventWriter.ConnectAsync(
                command.PipeId,
                CancellationToken.None).ConfigureAwait(false);
            BrokerDiagnosticsLog.Record("pipe-connected");
        }
        catch (BrokerPipeException)
        {
            BrokerDiagnosticsLog.Record("pipe-connect-failed");
            return BrokerExitCode.PipeConnectionFailed;
        }

        await using (events.ConfigureAwait(false))
        {
            try
            {
                ElevationGuard.EnsureElevated();
                BrokerDiagnosticsLog.Record("elevation-confirmed");
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
                BrokerDiagnosticsLog.Record("request-rejected");
                return PublishFailure(
                    events,
                    BrokerEventKind.Rejected,
                    exception.Message,
                    exception.ErrorCode,
                    BrokerExitCode.RequestRejected);
            }
            catch (BrokerNotElevatedException)
            {
                BrokerDiagnosticsLog.Record("elevation-failed");
                return PublishFailure(
                    events,
                    BrokerEventKind.Failed,
                    "O broker não recebeu um token administrativo válido.",
                    "broker-not-elevated",
                    BrokerExitCode.NotElevated);
            }
            catch (BrokerPipeException)
            {
                BrokerDiagnosticsLog.Record("pipe-disconnected");
                return BrokerExitCode.PipeConnectionFailed;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException
                or StackOverflowException
                or AccessViolationException))
            {
                BrokerDiagnosticsLog.Record("unhandled-exception");
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
        BrokerDiagnosticsLog.Record("request-loaded", plan.PlanId);
        var validated = new PlanValidator().Validate(plan);
        BrokerDiagnosticsLog.Record("plan-validated", plan.PlanId);

        events.Publish(
            BrokerEventKind.Started,
            "Plano administrativo validado. Iniciando transação elevada.",
            item => item.TransactionId = plan.PlanId);

        BrokerDiagnosticsLog.Record("action-started", plan.PlanId);
        var runtime = WindowsAdministratorRuntimeAdapter.CreateDefault();
        using var timeout = new CancellationTokenSource(ExecutionTimeout);
        WindowsTransactionResult result;
        try
        {
            result = await runtime.ExecuteAsync(
                validated,
                events,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            BrokerDiagnosticsLog.Record("execution-timeout", plan.PlanId);
            return PublishFailure(
                events,
                BrokerEventKind.Failed,
                "A etapa administrativa excedeu o tempo seguro e foi interrompida. Consulte o relatório antes de tentar novamente.",
                "broker-operation-timeout",
                BrokerExitCode.ExecutionFailed);
        }

        if (timeout.IsCancellationRequested)
        {
            BrokerDiagnosticsLog.Record("execution-timeout", plan.PlanId);
            return PublishFailure(
                events,
                BrokerEventKind.Failed,
                "A etapa administrativa excedeu o tempo seguro e foi interrompida. Consulte o relatório antes de tentar novamente.",
                "broker-operation-timeout",
                BrokerExitCode.ExecutionFailed);
        }

        BrokerDiagnosticsLog.Record("journal-saved", plan.PlanId);
        if (result.State != WindowsTransactionState.Committed)
        {
            BrokerDiagnosticsLog.Record("execution-failed", plan.PlanId);
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
            BrokerDiagnosticsLog.Record("terminal-event-sent", plan.PlanId);
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
        BrokerDiagnosticsLog.Record("terminal-event-sent", plan.PlanId);
        return BrokerExitCode.Success;
    }

    private static async Task<int> RollbackAsync(
        BrokerCommand command,
        NamedPipeEventWriter events)
    {
        var transactionId = command.RollbackTransactionId!.Value;
        BrokerDiagnosticsLog.Record("rollback-requested", transactionId);
        events.Publish(
            BrokerEventKind.RollbackStarted,
            "Iniciando reversão da transação elevada.",
            item => item.TransactionId = transactionId);

        var runtime = WindowsAdministratorRuntimeAdapter.CreateDefault();
        using var timeout = new CancellationTokenSource(ExecutionTimeout);
        WindowsTransactionResult result;
        try
        {
            result = await runtime.RollbackAsync(
                transactionId,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            BrokerDiagnosticsLog.Record("rollback-timeout", transactionId);
            return PublishFailure(
                events,
                BrokerEventKind.Failed,
                "A reversão excedeu o tempo seguro e foi interrompida. O journal preserva o estado para diagnóstico.",
                "broker-rollback-timeout",
                BrokerExitCode.RollbackFailed);
        }

        if (result.State != WindowsTransactionState.RolledBack)
        {
            BrokerDiagnosticsLog.Record("rollback-failed", transactionId);
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

        BrokerDiagnosticsLog.Record("rollback-completed", transactionId);

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
