using System.IO;
using FiveMCleaner.App.Services;
using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class TelemetryErrorClassifierTests
{
    [Theory]
    [InlineData(typeof(TimeoutException), "timeout")]
    [InlineData(typeof(UnauthorizedAccessException), "access-denied")]
    [InlineData(typeof(IOException), "io")]
    [InlineData(typeof(InvalidDataException), "invalid-data")]
    public void ClassifyException_UsesOnlyFixedCategories(Type exceptionType, string expected)
    {
        var exception = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(exceptionType)!);

        Assert.Equal(expected, TelemetryErrorClassifier.ClassifyException(exception));
    }

    [Fact]
    public void ClassifyException_UnknownExceptionType_FallsBackToUnexpected()
    {
        Assert.Equal("unexpected", TelemetryErrorClassifier.ClassifyException(new InvalidOperationException()));
    }

    [Fact]
    public void ClassifyException_OperationCanceled_MapsToCancelled()
    {
        Assert.Equal("cancelled", TelemetryErrorClassifier.ClassifyException(new OperationCanceledException()));
    }
}

public sealed class DisabledAnonymousTelemetryServiceTests
{
    [Fact]
    public void Instance_IsNeverEnabledAndNeverThrows()
    {
        var service = DisabledAnonymousTelemetryService.Instance;

        Assert.False(service.IsEnabled);
        service.SetEnabled(true);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public async Task TrackAsync_CompletesWithoutSendingAnything()
    {
        var service = DisabledAnonymousTelemetryService.Instance;

        await service.TrackAsync(new AnonymousTelemetryEvent("optimization-completed", TimeSpan.Zero, "1.0.0"));
    }
}
