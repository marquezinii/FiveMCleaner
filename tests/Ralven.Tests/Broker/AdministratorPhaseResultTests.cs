using Ralven.Contracts;
using Ralven.Windows.Engine;
using Xunit;
using BrokerProgram = Ralven.Broker.Program;

namespace Ralven.Tests.Broker;

public sealed class AdministratorPhaseResultTests
{
    [Theory]
    [InlineData(TransactionState.Committed, true)]
    [InlineData(TransactionState.CommittedWithErrors, true)]
    [InlineData(TransactionState.AwaitingElevation, false)]
    [InlineData(TransactionState.Failed, false)]
    [InlineData(TransactionState.RollbackFailed, false)]
    public void Execution_ReportsOnlyConfirmedAdministratorCompletion(TransactionState state, bool expected)
    {
        var result = Result(state);
        Assert.Equal(expected, BrokerProgram.IsAdministratorExecutionComplete(result));
        Assert.False(BrokerProgram.IsAdministratorExecutionComplete(result with { Error = "Administrator failure" }));
        Assert.False(BrokerProgram.IsAdministratorExecutionComplete(result with { DeferredAdministratorActionIds = ["pending"] }));
    }

    [Theory]
    [InlineData(TransactionState.RolledBack, true)]
    [InlineData(TransactionState.AwaitingStandardRollback, true)]
    [InlineData(TransactionState.CommittedWithErrors, true)]
    [InlineData(TransactionState.AwaitingElevationRollback, false)]
    [InlineData(TransactionState.RollbackFailed, false)]
    [InlineData(TransactionState.RollingBack, false)]
    public void Rollback_DistinguishesAdministratorCompletionFromRemainingUserActions(TransactionState state, bool expected)
    {
        var result = Result(state);
        Assert.Equal(expected, BrokerProgram.IsAdministratorRollbackComplete(result));
        Assert.False(BrokerProgram.IsAdministratorRollbackComplete(result with { Error = "Administrator rollback failed" }));
    }

    private static WindowsTransactionResult Result(TransactionState state) => new()
    {
        TransactionId = Guid.NewGuid(),
        State = state,
        AppliedActionIds = [],
        ChangedActionIds = [],
        DeferredAdministratorActionIds = []
    };
}
