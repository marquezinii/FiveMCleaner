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
        var environment = TestEnvironment;
        using var guard = new SingleInstanceGuard(environment);

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
        var environment = TestEnvironment;
        using var second = new SingleInstanceGuard(environment);
        var firstAcquired = new ManualResetEventSlim();
        var releaseFirst = new ManualResetEventSlim();

        var holderThread = new Thread(() =>
        {
            using var first = new SingleInstanceGuard(environment);
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
        var environment = TestEnvironment;
        var first = new SingleInstanceGuard(environment);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var second = new SingleInstanceGuard(environment);
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void Dispose_WithoutEverAcquiring_DoesNotThrow()
    {
        var environment = TestEnvironment;
        var guard = new SingleInstanceGuard(environment);

        var exception = Record.Exception(guard.Dispose);

        Assert.Null(exception);
    }

    /// <summary>
    /// Named Mutexes are process- and session-wide. Every acquiring test
    /// below releases its guard(s) via <c>using</c>/explicit
    /// <see cref="SingleInstanceGuard.Dispose"/> before returning, so as long
    /// as xUnit does not run methods within this class in parallel (the
    /// default), reusing the same environment/name across tests is safe.
    /// Running a real FiveMCleaner Development instance at the same time as
    /// this test suite could theoretically collide; that is an acceptable,
    /// narrow window for a local test run.
    /// </summary>
    private static AppRuntimeEnvironment TestEnvironment => AppRuntimeEnvironment.Development;
}
