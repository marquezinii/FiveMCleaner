using System.Diagnostics;
using System.Text.Json;
using Ralven.UpdateRuntime;
using Xunit;

namespace Ralven.Tests.UpdateRuntime;

public sealed class UpdaterDiagnosticsStartupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "RalvenStartupDiagnostics", Guid.NewGuid().ToString("N"));
    private static readonly UpdaterEvent Event = new("startup-test", "health-check", "completed",
        UpdaterEventCodes.HealthConfirmed, "1.0.0", "1.1.0", "Development");

    [Fact]
    public async Task DeferredRecord_DurablyQueuesWithoutWaitingForTheTransport()
    {
        var transportEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransport = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        Task<bool> Send(UpdaterEvent value)
        {
            Interlocked.Increment(ref sends);
            transportEntered.TrySetResult();
            return releaseTransport.Task;
        }
        var immediate = new UpdaterDiagnostics(Path.Combine(root, "immediate"), Send);
        var deferredRoot = Path.Combine(root, "deferred");
        var deferred = new UpdaterDiagnostics(deferredRoot, Send);
        var before = Stopwatch.StartNew();
        var blocked = immediate.RecordAsync(Event, "local-only", telemetryAuthorized: true);
        try
        {
            await transportEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var after = Stopwatch.StartNew();
            await deferred.RecordAsync(Event, "local-only", telemetryAuthorized: true, flushPending: false);
            after.Stop();
            Assert.False(blocked.IsCompleted);
            Assert.Equal(1, sends);
            var pending = Directory.GetFiles(Path.Combine(deferredRoot, "UpdaterTelemetry", "pending"));
            Assert.Single(pending);
            var json = await File.ReadAllTextAsync(pending[0], TestContext.Current.CancellationToken);
            Assert.Equal(Event, JsonSerializer.Deserialize<UpdaterEvent>(json));
            Assert.DoesNotContain("local-only", json);
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"Deferred local enqueue: {after.Elapsed.TotalMilliseconds:F1} ms; immediate delivery still blocked after {before.Elapsed.TotalMilliseconds:F1} ms (controlled transport, no network).");
        }
        finally
        {
            releaseTransport.TrySetResult(true);
            await blocked;
        }
        await deferred.FlushPendingAsync(telemetryAuthorized: true);
        Assert.Equal(2, sends);
        Assert.Empty(Directory.GetFiles(Path.Combine(deferredRoot, "UpdaterTelemetry", "pending")));
    }

    [Fact]
    public void LocalRecord_KeepsTheLifecycleMutexOnItsOwnerThreadUntilSupervisionEnds()
    {
        var runtimeRoot = Path.Combine(root, "Runtime");
        var diagnostics = new UpdaterDiagnostics(root, _ => throw new InvalidOperationException("Network during supervision"));
        using (var lease = RuntimeUpdateLease.TryAcquire(runtimeRoot))
        {
            Assert.NotNull(lease);
            diagnostics.RecordAsync(Event, null, telemetryAuthorized: true, flushPending: false).GetAwaiter().GetResult();
            Assert.False(TryAcquireOnAnotherThread(runtimeRoot));
        }
        Assert.True(TryAcquireOnAnotherThread(runtimeRoot));
    }

    private static bool TryAcquireOnAnotherThread(string runtimeRoot)
    {
        var acquired = false;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var lease = RuntimeUpdateLease.TryAcquire(runtimeRoot);
                acquired = lease is not null;
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        return acquired;
    }

    [Fact]
    public async Task DeferredRecord_WithoutConsentDeletesPendingWithoutSending()
    {
        var diagnostics = new UpdaterDiagnostics(root, _ => throw new InvalidOperationException("Unauthorized network"));
        await diagnostics.RecordAsync(Event, null, telemetryAuthorized: true, flushPending: false);
        await diagnostics.RecordAsync(Event, null, telemetryAuthorized: false, flushPending: false);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "UpdaterTelemetry", "pending")));
        Assert.True(File.Exists(Path.Combine(root, "Logs", "updater.jsonl")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
