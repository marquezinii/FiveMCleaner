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
        firstAcquired.Wait();

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
