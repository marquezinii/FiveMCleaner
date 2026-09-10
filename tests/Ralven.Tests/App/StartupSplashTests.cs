using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

[Collection(WpfApplicationCollection.Name)]
public sealed class StartupSplashTests
{
    public StartupSplashTests()
    {
        // Production creates Application before the splash, registering WPF's
        // pack resource loader. Keep the same prerequisite in an isolated test.
        if (Application.Current is not null) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "WPF application initialization timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public async Task FastStartupCompletesWithoutShowingOrCancelling()
    {
        var cancelled = false;
        using var splash = new StartupSplash("Initializing", "Close", false,
            () => cancelled = true, TimeSpan.FromSeconds(30));
        splash.Dispose();
        await splash.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(splash.WasShown);
        Assert.False(cancelled);
        splash.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibleSplashClosesWithoutCancellingStartup(bool lightTheme)
    {
        var cancelled = false;
        using var splash = new StartupSplash("Initializing", "Close", lightTheme,
            () => cancelled = true, TimeSpan.Zero);
        await WaitUntilShown(splash);
        splash.Dispose();
        await splash.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(cancelled);
    }

    [Fact]
    public async Task CompletionDuringWindowPreparationDoesNotFlashOrCancel()
    {
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var previousObserver = StartupTrace.Observer;
        var cancelled = false;
        StartupTrace.Observer = stage =>
        {
            if (stage != "splash-prepared") return;
            prepared.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        };
        try
        {
            using var splash = new StartupSplash("Initializing", "Close", false,
                () => cancelled = true, TimeSpan.Zero);
            await prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
            splash.Dispose();
            release.Set();
            await splash.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(splash.WasShown);
            Assert.False(cancelled);
        }
        finally
        {
            release.Set();
            StartupTrace.Observer = previousObserver;
        }
    }

    [Fact]
    public async Task UserCloseCancelsAndTerminatesThePresentation()
    {
        var cancelled = false;
        using var splash = new StartupSplash("Initializing", "Close", false,
            () => cancelled = true, TimeSpan.Zero);
        await WaitUntilShown(splash);
        var window = (Window)typeof(StartupSplash).GetField("window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(splash)!;
        await window.Dispatcher.InvokeAsync(window.Close);
        await splash.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancelled);
    }

    [Fact]
    public async Task PresentationFailureClosesWithoutCancellingStartup()
    {
        var previousObserver = StartupTrace.Observer;
        var cancelled = false;
        StartupTrace.Observer = stage =>
        {
            if (stage == "splash-prepared") throw new InvalidOperationException("Preparation failure");
        };
        try
        {
            using var splash = new StartupSplash("Initializing", "Close", false,
                () => cancelled = true, TimeSpan.Zero);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                splash.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("Preparation failure", error.Message);
            Assert.False(splash.WasShown);
            Assert.False(cancelled);
        }
        finally { StartupTrace.Observer = previousObserver; }
    }

    [Fact]
    public void InvalidThresholdFailsBeforeStartingAThread()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StartupSplash("Initializing", "Close", false,
            () => { }, TimeSpan.FromMilliseconds(-1)));
    }

    private static async Task WaitUntilShown(StartupSplash splash)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!splash.WasShown)
        {
            if (splash.Completion.IsCompleted)
            {
                await splash.Completion;
                Assert.Fail("Splash terminated before it was shown.");
            }
            await Task.Delay(10, timeout.Token);
        }
    }
}
