using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class SingleInstanceGuardTests
{
    [Theory]
    [InlineData(AppRuntimeEnvironment.Development)]
    [InlineData(AppRuntimeEnvironment.Production)]
    public void BuildMutexName_IncludesTheEnvironmentSoDevAndProdNeverCollide(AppRuntimeEnvironment environment)
    {
        var name = SingleInstanceGuard.BuildMutexName(environment);

        Assert.Contains(environment.ToString(), name, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMutexName_DevelopmentAndProductionProduceDifferentNames()
    {
        var developmentName = SingleInstanceGuard.BuildMutexName(AppRuntimeEnvironment.Development);
        var productionName = SingleInstanceGuard.BuildMutexName(AppRuntimeEnvironment.Production);

        Assert.NotEqual(developmentName, productionName);
    }

    [Theory]
    [InlineData(AppRuntimeEnvironment.Development)]
    [InlineData(AppRuntimeEnvironment.Production)]
    public void BuildActivationEventName_IncludesTheEnvironment(AppRuntimeEnvironment environment)
    {
        var name = SingleInstanceGuard.BuildActivationEventName(environment);

        Assert.Contains(environment.ToString(), name, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildActivationEventName_DevelopmentAndProductionProduceDifferentNames()
    {
        var developmentName = SingleInstanceGuard.BuildActivationEventName(AppRuntimeEnvironment.Development);
        var productionName = SingleInstanceGuard.BuildActivationEventName(AppRuntimeEnvironment.Production);

        Assert.NotEqual(developmentName, productionName);
    }

    [Fact]
    public void RequestActivation_IsObservedByARunningListener()
    {
        // RequestActivation is deliberately called without acquiring the
        // mutex here: within one test process the mutex is thread-reentrant,
        // so a second TryAcquire from this same thread would trivially
        // succeed. Activation signaling is independent of mutex ownership,
        // which is what this test exercises.
        var mutexName = NewTestMutexName();
        using var runningGuard = new SingleInstanceGuard(mutexName);
        Assert.True(runningGuard.TryAcquire());

        using var activationObserved = new ManualResetEventSlim();
        runningGuard.ListenForActivation(activationObserved.Set);

        var second = new SingleInstanceGuard(mutexName);
        try
        {
            second.RequestActivation();
        }
        finally
        {
            second.Dispose();
        }

        Assert.True(
            activationObserved.Wait(TimeSpan.FromSeconds(5), cancellationToken: global::Xunit.TestContext.Current.CancellationToken),
            "The running instance should have observed the activation request.");
    }

    [Fact]
    public void RequestActivation_IssuedBeforeListeningIsNotLost()
    {
        // The activation event is auto-reset and created as soon as the
        // guard wins the mutex, so a request that lands before the listener
        // thread is up stays signaled until a waiter consumes it.
        var mutexName = NewTestMutexName();
        using var runningGuard = new SingleInstanceGuard(mutexName);
        Assert.True(runningGuard.TryAcquire());

        var second = new SingleInstanceGuard(mutexName);
        try
        {
            second.RequestActivation();
        }
        finally
        {
            second.Dispose();
        }

        using var activationObserved = new ManualResetEventSlim();
        runningGuard.ListenForActivation(activationObserved.Set);

        Assert.True(
            activationObserved.Wait(TimeSpan.FromSeconds(5), cancellationToken: global::Xunit.TestContext.Current.CancellationToken),
            "A request made before listening should still be observed.");
    }

    [Fact]
    public void TryAcquire_FirstCaller_Succeeds()
    {
        var mutexName = NewTestMutexName();
        using var guard = new SingleInstanceGuard(mutexName);

        Assert.True(guard.TryAcquire());
    }

    [Fact]
    public void TryAcquire_SecondCallerWhileFirstStillHolding_Fails()
    {
        // A named Mutex is reentrant for its owning *thread*, so acquiring
        // it twice from this same test thread would trivially "succeed"
        // both times and prove nothing -- real duplicate-instance
        // contention is always across different processes (different
        // threads). Holding the first guard on a background thread
        // reproduces that genuine cross-thread blocking behavior.
        var mutexName = NewTestMutexName();
        using var second = new SingleInstanceGuard(mutexName);
        var firstAcquired = new ManualResetEventSlim();
        var releaseFirst = new ManualResetEventSlim();

        var holderThread = new Thread(() =>
        {
            using var first = new SingleInstanceGuard(mutexName);
            first.TryAcquire();
            firstAcquired.Set();
            releaseFirst.Wait();
        })
        {
            IsBackground = true
        };
        holderThread.Start();
        firstAcquired.Wait(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        try
        {
            Assert.False(second.TryAcquire());
        }
        finally
        {
            releaseFirst.Set();
            holderThread.Join();
        }
    }

    [Fact]
    public void TryAcquire_AfterTheFirstInstanceDisposes_SucceedsForANewOne()
    {
        var mutexName = NewTestMutexName();
        var first = new SingleInstanceGuard(mutexName);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var second = new SingleInstanceGuard(mutexName);
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void Dispose_WithoutEverAcquiring_DoesNotThrow()
    {
        var mutexName = NewTestMutexName();
        var guard = new SingleInstanceGuard(mutexName);

        var exception = Record.Exception(guard.Dispose);

        Assert.Null(exception);
    }

    private static string NewTestMutexName() =>
        $"Local\\FiveMCleaner.SingleInstance.Test_{Guid.NewGuid():N}";
}
