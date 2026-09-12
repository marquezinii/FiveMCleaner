using Ralven.Contracts;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class WindowsSystemHealthInspectorTests
{
    [Fact]
    public async Task Inspector_WhenNativeCallThrows_ProviderHealthCarriesClassifiedBugCode()
    {
        var inspector = new WindowsSystemHealthInspector(Read);

        var snapshot = await inspector.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BugCode.SEC_HEALTH_QUERY, snapshot.Antivirus.BugCode);
        Assert.Equal(WindowsSecurityHealthState.Unavailable, snapshot.Antivirus.State);
        return;

        int Read(
            WindowsSystemHealthInspector.SecurityProvider _,
            out WindowsSystemHealthInspector.NativeSecurityProviderHealth health)
        {
            health = default;
            throw new DllNotFoundException("wscapi.dll missing");
        }
    }

    [Theory]
    [InlineData(
        0,
        WindowsSecurityHealthState.Good)]
    [InlineData(
        1,
        WindowsSecurityHealthState.NotMonitored)]
    [InlineData(
        2,
        WindowsSecurityHealthState.Poor)]
    [InlineData(
        3,
        WindowsSecurityHealthState.Snoozed)]
    public async Task InspectAsync_MapsNativeHealth(
        int nativeHealthValue,
        WindowsSecurityHealthState expected)
    {
        var inspector = new WindowsSystemHealthInspector(Read);

        var snapshot = await inspector.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, snapshot.Antivirus.State);
        Assert.Equal(expected, snapshot.Firewall.State);
        Assert.Equal(expected, snapshot.AutomaticUpdates.State);
        Assert.All(
            [snapshot.Antivirus, snapshot.Firewall, snapshot.AutomaticUpdates],
            result => Assert.Equal(0, result.HResult));
        Assert.False(snapshot.IsPartial);
        Assert.NotEqual(default, snapshot.ObservedAtUtc);
        return;

        int Read(
            WindowsSystemHealthInspector.SecurityProvider _,
            out WindowsSystemHealthInspector.NativeSecurityProviderHealth health)
        {
            health = (WindowsSystemHealthInspector.NativeSecurityProviderHealth)nativeHealthValue;
            return 0;
        }
    }

    [Fact]
    public async Task InspectAsync_QueriesProvidersIndependentlyAndMarksPartialSnapshot()
    {
        var queried = new List<WindowsSystemHealthInspector.SecurityProvider>();
        var inspector = new WindowsSystemHealthInspector(Read);

        var snapshot = await inspector.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                WindowsSystemHealthInspector.SecurityProvider.Antivirus,
                WindowsSystemHealthInspector.SecurityProvider.Firewall,
                WindowsSystemHealthInspector.SecurityProvider.AutoUpdateSettings
            ],
            queried);
        Assert.Equal(WindowsSecurityHealthState.Good, snapshot.Antivirus.State);
        Assert.Equal(WindowsSecurityHealthState.Unavailable, snapshot.Firewall.State);
        Assert.Equal(unchecked((int)0x80004005), snapshot.Firewall.HResult);
        Assert.Equal(WindowsSecurityHealthState.Snoozed, snapshot.AutomaticUpdates.State);
        Assert.True(snapshot.IsPartial);
        return;

        int Read(
            WindowsSystemHealthInspector.SecurityProvider provider,
            out WindowsSystemHealthInspector.NativeSecurityProviderHealth health)
        {
            queried.Add(provider);
            health = provider switch
            {
                WindowsSystemHealthInspector.SecurityProvider.AutoUpdateSettings =>
                    WindowsSystemHealthInspector.NativeSecurityProviderHealth.Snooze,
                _ => WindowsSystemHealthInspector.NativeSecurityProviderHealth.Good
            };

            return provider == WindowsSystemHealthInspector.SecurityProvider.Firewall
                ? unchecked((int)0x80004005)
                : 0;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(unchecked((int)0x80070424))]
    public async Task InspectAsync_DoesNotTreatNonSuccessAsNativePoor(int hResult)
    {
        var inspector = new WindowsSystemHealthInspector(Read);

        var snapshot = await inspector.InspectAsync(TestContext.Current.CancellationToken);

        Assert.All(
            [snapshot.Antivirus, snapshot.Firewall, snapshot.AutomaticUpdates],
            result =>
            {
                Assert.Equal(WindowsSecurityHealthState.Unavailable, result.State);
                Assert.Equal(hResult, result.HResult);
            });
        Assert.True(snapshot.IsPartial);
        return;

        int Read(
            WindowsSystemHealthInspector.SecurityProvider _,
            out WindowsSystemHealthInspector.NativeSecurityProviderHealth health)
        {
            health = WindowsSystemHealthInspector.NativeSecurityProviderHealth.Poor;
            return hResult;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task InspectAsync_HonorsCancellationAroundIndependentQueries(
        int cancelOnQuery)
    {
        using var cancellation = new CancellationTokenSource();
        var queryCount = 0;
        var inspector = new WindowsSystemHealthInspector(Read);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inspector.InspectAsync(cancellation.Token));

        Assert.Equal(cancelOnQuery, queryCount);
        return;

        int Read(
            WindowsSystemHealthInspector.SecurityProvider _,
            out WindowsSystemHealthInspector.NativeSecurityProviderHealth health)
        {
            queryCount++;
            health = WindowsSystemHealthInspector.NativeSecurityProviderHealth.Good;
            if (queryCount == cancelOnQuery)
            {
                cancellation.Cancel();
            }

            return 0;
        }
    }
}
