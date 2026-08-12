using System.Net;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class RamBucketCalculatorTests
{
    [Theory]
    [InlineData(1.5, 2)]
    [InlineData(2.0, 2)]
    [InlineData(3.9, 4)]
    [InlineData(7.92, 8)]
    [InlineData(15.92, 16)]
    [InlineData(31.5, 32)]
    [InlineData(64.0, 64)]
    [InlineData(200.0, 256)]
    [InlineData(9000.0, 256)]
    public void ComputeBucketGiB_RoundsUpToTheNearestAllowlistedBucket(double exact, int expectedBucket)
    {
        Assert.Equal(expectedBucket, RamBucketCalculator.ComputeBucketGiB(exact));
    }
}

public sealed class TelemetryEventValidatorTests
{
    private static AnonymousTelemetryEvent ValidEvent() => new(
        "optimization-completed",
        TimeSpan.FromMilliseconds(18_342),
        "1.0.4",
        OsVersion: "Windows 11",
        SystemArchitecture: "x64",
        CpuModel: "AMD Ryzen 5 5600X",
        GpuModel: "NVIDIA GeForce RTX 5070",
        RamBucketGiB: 32,
        Profile: "Balanced",
        ActionIds: ["fivem.legacy.cache.repair", "windows.power-plan.session"]);

    [Fact]
    public void Validate_WellFormedEvent_DoesNotThrow()
    {
        var exception = Record.Exception(() => TelemetryEventValidator.Validate(ValidEvent()));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_UnknownEventName_Throws()
    {
        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(ValidEvent() with { EventName = "unknown" }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(255)]
    public void Validate_RamBucketNotInAllowlist_Throws(int bucket)
    {
        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(ValidEvent() with { RamBucketGiB = bucket }));
    }

    [Fact]
    public void Validate_UnknownProfile_Throws()
    {
        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(ValidEvent() with { Profile = "Ultra" }));
    }

    [Fact]
    public void Validate_TooManyActionIds_Throws()
    {
        var tooMany = Enumerable.Range(0, 31).Select(i => $"action.{i}").ToArray();

        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(ValidEvent() with { ActionIds = tooMany }));
    }

    [Fact]
    public void Validate_ActionIdWithFreeTextCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(
            ValidEvent() with { ActionIds = ["C:\\Users\\someone\\file.txt"] }));
    }

    [Fact]
    public void Validate_CpuOrGpuModelWithControlCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => TelemetryEventValidator.Validate(ValidEvent() with { CpuModel = "AMD\nRyzen" }));
    }

    [Fact]
    public void Validate_NullOptionalFields_DoesNotThrow()
    {
        var minimal = new AnonymousTelemetryEvent("optimization-cancelled", TimeSpan.Zero, "1.0.4", "cancelled");

        var exception = Record.Exception(() => TelemetryEventValidator.Validate(minimal));

        Assert.Null(exception);
    }
}

public sealed class LocalTelemetryQueueTests : IDisposable
{
    private readonly string tempDirectory =
        Path.Combine(Path.GetTempPath(), "FiveMCleanerTelemetryQueueTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static AnonymousTelemetryEvent SampleEvent(string appVersion = "1.0.4") => new(
        "optimization-completed", TimeSpan.FromSeconds(5), appVersion);

    [Fact]
    public async Task EnqueueAsync_ThenReadPending_RoundTripsTheEvent()
    {
        var queue = new LocalTelemetryQueue(tempDirectory);

        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        var pending = queue.ReadPending(10);

        var item = Assert.Single(pending);
        Assert.Equal("optimization-completed", item.Event.EventName);
    }

    [Fact]
    public async Task ReadPending_ReturnsEventsInChronologicalOrder()
    {
        var queue = new LocalTelemetryQueue(tempDirectory);

        await queue.EnqueueAsync(SampleEvent("1.0.1"), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(SampleEvent("1.0.2"), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(SampleEvent("1.0.3"), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        var pending = queue.ReadPending(10);

        Assert.Equal(["1.0.1", "1.0.2", "1.0.3"], pending.Select(item => item.Event.AppVersion));
    }

    [Fact]
    public async Task ReadPending_RespectsTheMaxCountLimit()
    {
        var queue = new LocalTelemetryQueue(tempDirectory);
        for (var i = 0; i < 5; i++)
        {
            await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, queue.ReadPending(2).Count);
    }

    [Fact]
    public void Remove_DeletesTheFile()
    {
        var queue = new LocalTelemetryQueue(tempDirectory);
        Directory.CreateDirectory(tempDirectory);
        var filePath = Path.Combine(tempDirectory, "test.json");
        File.WriteAllText(filePath, "{}");

        queue.Remove(filePath);

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task ReadPending_DropsACorruptFileInsteadOfBlockingForever()
    {
        var queue = new LocalTelemetryQueue(tempDirectory);
        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        Directory.CreateDirectory(tempDirectory);
        var corruptFile = Path.Combine(tempDirectory, "0_corrupt.json");
        await File.WriteAllTextAsync(corruptFile, "{ not valid json", cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        var pending = queue.ReadPending(10);

        Assert.Single(pending);
        Assert.False(File.Exists(corruptFile));
    }

    [Fact]
    public void ReadPending_NoDirectoryYet_ReturnsEmpty()
    {
        var queue = new LocalTelemetryQueue(tempDirectory);

        Assert.Empty(queue.ReadPending(10));
    }

    [Fact]
    public async Task Prune_DropsTheOldestEventsOnceTheCountCeilingIsExceeded()
    {
        // Age alone never bounded the queue: a run enqueues events but a flush
        // only drains one batch, so a long offline period grew the queue for
        // the whole retention window with nothing to stop it.
        var queue = new LocalTelemetryQueue(tempDirectory);
        for (var index = 0; index < 8; index++)
        {
            await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        }

        var oldest = Directory.GetFiles(tempDirectory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        queue.Prune(TimeSpan.FromDays(14), maxFiles: 3);

        Assert.Equal(3, Directory.GetFiles(tempDirectory, "*.json").Length);
        Assert.All(oldest, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public async Task Prune_KeepsEverythingWhenTheQueueIsWithinBothBounds()
    {
        var queue = new LocalTelemetryQueue(tempDirectory);
        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        queue.Prune(TimeSpan.FromDays(14), maxFiles: 200);

        Assert.Equal(2, queue.ReadPending(10).Count);
    }

    [Fact]
    public async Task Remove_DoesNotThrowWhenTheQueuedFileCannotBeDeleted()
    {
        // Regression guard: File.Delete throws UnauthorizedAccessException
        // (not IOException) for a read-only file. Remove() runs right after a
        // *successful* send, so letting that escape resurfaced the very same
        // event on every later flush.
        var queue = new LocalTelemetryQueue(tempDirectory);
        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        var filePath = Directory.GetFiles(tempDirectory, "*.json").Single();
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        try
        {
            queue.Remove(filePath);
        }
        finally
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
    }
}

public sealed class CloudflareTelemetryTransportTests
{
    private static readonly Uri TestEndpoint = new("https://telemetry.example.workers.dev/v1/events", UriKind.Absolute);

    private static AnonymousTelemetryEvent SampleEvent() => new(
        "optimization-completed",
        TimeSpan.FromSeconds(5),
        "1.0.4",
        OsVersion: "Windows 11",
        CpuModel: "AMD Ryzen 5 5600X");

    [Fact]
    public async Task SendBatchAsync_EmptyBatch_ReturnsTrueWithoutSendingARequest()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var transport = new CloudflareTelemetryTransport(client, TestEndpoint);

        var result = await transport.SendBatchAsync([], cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendBatchAsync_SuccessfulResponse_ReturnsTrueAndPostsTheWholeBatch()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var transport = new CloudflareTelemetryTransport(client, TestEndpoint);

        var result = await transport.SendBatchAsync([SampleEvent(), SampleEvent() with { AppVersion = "1.0.5" }], cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Contains("1.0.4", handler.Body, StringComparison.Ordinal);
        Assert.Contains("1.0.5", handler.Body, StringComparison.Ordinal);
        Assert.Contains("AMD Ryzen 5 5600X", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"environment\":\"Production\"", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task SendBatchAsync_AlwaysIncludesTheConfiguredRuntimeEnvironment(string environment)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var transport = new CloudflareTelemetryTransport(client, TestEndpoint, environment);

        await transport.SendBatchAsync([SampleEvent()], cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Contains($"\"environment\":\"{environment}\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsAnEnvironmentTheWorkerWillNotAccept()
    {
        using var client = new HttpClient(new RecordingHandler());

        Assert.Throws<ArgumentException>(() => new CloudflareTelemetryTransport(client, TestEndpoint, "Staging"));
    }

    [Fact]
    public async Task SendBatchAsync_FailedResponse_ReturnsFalse()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler);
        var transport = new CloudflareTelemetryTransport(client, TestEndpoint);

        var result = await transport.SendBatchAsync([SampleEvent()], cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task SendBatchAsync_AcceptsTheWorker202Response()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler);
        var transport = new CloudflareTelemetryTransport(client, TestEndpoint);

        var result = await transport.SendBatchAsync([SampleEvent()], cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task SendBatchAsync_NetworkFailure_ReturnsFalseInsteadOfThrowing()
    {
        var handler = new ThrowingHandler();
        using var client = new HttpClient(handler);
        var transport = new CloudflareTelemetryTransport(client, TestEndpoint);

        var result = await transport.SendBatchAsync([SampleEvent()], cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public void Constructor_RejectsANonHttpsEndpoint()
    {
        using var client = new HttpClient(new RecordingHandler());

        Assert.Throws<ArgumentException>(() => new CloudflareTelemetryTransport(client, new Uri("http://insecure.example.com")));
    }
}

public sealed class QueuedCloudflareTelemetryServiceTests : IDisposable
{
    private readonly string tempDirectory =
        Path.Combine(Path.GetTempPath(), "FiveMCleanerQueuedTelemetryTests_" + Guid.NewGuid().ToString("N"));
    private static readonly Uri TestEndpoint = new("https://telemetry.example.workers.dev/v1/events", UriKind.Absolute);

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static AnonymousTelemetryEvent SampleEvent() => new(
        "optimization-completed", TimeSpan.FromSeconds(5), "1.0.4");

    [Fact]
    public async Task TrackAsync_DoesNothingUntilEnabled()
    {
        var handler = new CountingHandler(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler);
        var service = new QueuedCloudflareTelemetryService(
            new LocalTelemetryQueue(tempDirectory),
            new CloudflareTelemetryTransport(client, TestEndpoint));

        await service.TrackAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: global::Xunit.TestContext.Current.CancellationToken); // let the fire-and-forget flush attempt (if any) settle

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TrackAsync_ThenFlushPendingAsync_SendsAndClearsTheQueueOnSuccess()
    {
        var handler = new CountingHandler(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler);
        var queue = new LocalTelemetryQueue(tempDirectory);
        var service = new QueuedCloudflareTelemetryService(queue, new CloudflareTelemetryTransport(client, TestEndpoint));
        service.SetEnabled(true);

        await service.TrackAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        await service.FlushPendingAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.True(handler.CallCount >= 1);
        Assert.Empty(queue.ReadPending(10));
    }

    [Fact]
    public async Task FlushPendingAsync_TransportFailure_KeepsTheEventQueued()
    {
        var handler = new CountingHandler(HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler);
        var queue = new LocalTelemetryQueue(tempDirectory);
        var service = new QueuedCloudflareTelemetryService(queue, new CloudflareTelemetryTransport(client, TestEndpoint));
        service.SetEnabled(true);

        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        await service.FlushPendingAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Single(queue.ReadPending(10));
    }

    [Fact]
    public async Task FlushPendingAsync_PermanentClientRejection_DropsTheRejectedEvent()
    {
        var handler = new CountingHandler(HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var queue = new LocalTelemetryQueue(tempDirectory);
        var service = new QueuedCloudflareTelemetryService(queue, new CloudflareTelemetryTransport(client, TestEndpoint));
        service.SetEnabled(true);
        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        await service.FlushPendingAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Empty(queue.ReadPending(10));
    }

    [Fact]
    public async Task FlushPendingAsync_EmptyQueue_DoesNotSendAnyRequest()
    {
        var handler = new CountingHandler(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler);
        var service = new QueuedCloudflareTelemetryService(
            new LocalTelemetryQueue(tempDirectory),
            new CloudflareTelemetryTransport(client, TestEndpoint));
        service.SetEnabled(true);

        await service.FlushPendingAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task FlushPendingAsync_DoesNotSendQueuedEventsWithoutConsent()
    {
        var handler = new CountingHandler(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler);
        var queue = new LocalTelemetryQueue(tempDirectory);
        var service = new QueuedCloudflareTelemetryService(queue, new CloudflareTelemetryTransport(client, TestEndpoint));
        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        await service.FlushPendingAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.CallCount);
        Assert.Single(queue.ReadPending(10));
    }

    [Fact]
    public async Task TrackAsync_InvalidEvent_ThrowsAndNeverQueuesIt()
    {
        var handler = new CountingHandler(HttpStatusCode.Accepted);
        using var client = new HttpClient(handler);
        var queue = new LocalTelemetryQueue(tempDirectory);
        var service = new QueuedCloudflareTelemetryService(queue, new CloudflareTelemetryTransport(client, TestEndpoint));
        service.SetEnabled(true);

        await Assert.ThrowsAsync<ArgumentException>(() => service.TrackAsync(SampleEvent() with { EventName = "not-allowed" }, cancellationToken: global::Xunit.TestContext.Current.CancellationToken));

        Assert.Empty(queue.ReadPending(10));
    }

    [Fact]
    public async Task ConcurrentFlushes_SendEachQueuedBatchOnlyOnce()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        var queue = new LocalTelemetryQueue(tempDirectory);
        var service = new QueuedCloudflareTelemetryService(queue, new CloudflareTelemetryTransport(client, TestEndpoint));
        service.SetEnabled(true);
        await queue.EnqueueAsync(SampleEvent(), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);

        var firstFlush = service.FlushPendingAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        await handler.Started.Task;
        var secondFlush = service.FlushPendingAsync(cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        handler.Release();
        await Task.WhenAll(firstFlush, secondFlush);

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(queue.ReadPending(10));
    }

    private sealed class CountingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }
        public TaskCompletionSource Started => started;

        public void Release() => release.SetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            started.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
