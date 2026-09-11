using System.Diagnostics;
using Ralven.Windows.Infrastructure;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class ResourceUsageInspectorTests
{
    [Fact]
    public void CalculateGpuUsage_MatchesInstanceNamesRegardlessOfOrderAndIgnoresUnpairedInstances()
    {
        var before = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase)
        {
            ["engine_a_engtype_3D"] = GpuSample(100, 1_000),
            ["engine_b_engtype_3D"] = GpuSample(500, 1_000),
            ["disappeared_engtype_3D"] = GpuSample(0, 1_000),
        };
        var after = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase)
        {
            ["ENGINE_B_ENGTYPE_3D"] = GpuSample(800, 2_000),
            ["new_engtype_3D"] = GpuSample(1_000, 2_000),
            ["engine_a_engtype_3D"] = GpuSample(300, 2_000),
        };

        var usage = WindowsResourceUsageInspector.CalculateGpuUsage(before, after);

        Assert.Equal(50d, usage);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CalculateGpuUsage_WithoutMatchingInstancesReturnsUnavailable(bool hasBefore, bool hasAfter)
    {
        var before = new Dictionary<string, CounterSample>();
        var after = new Dictionary<string, CounterSample>();
        if (hasBefore)
        {
            before["disappeared_engtype_3D"] = GpuSample(100, 1_000);
        }

        if (hasAfter)
        {
            after["new_engtype_3D"] = GpuSample(900, 2_000);
        }

        Assert.Null(WindowsResourceUsageInspector.CalculateGpuUsage(before, after));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(250, 400, 65)]
    [InlineData(600, 400, 100)]
    [InlineData(800, 700, 100)]
    [InlineData(-100, 400, 40)]
    [InlineData(-100, -200, 0)]
    public void CalculateGpuUsage_SumsNonnegativeReadingsAndClampsToPercentageRange(
        long firstDelta, long secondDelta, double expected)
    {
        var before = new Dictionary<string, CounterSample>
        {
            ["engine_a_engtype_3D"] = GpuSample(1_000, 1_000),
            ["engine_b_engtype_3D"] = GpuSample(1_000, 1_000),
        };
        var after = new Dictionary<string, CounterSample>
        {
            ["engine_a_engtype_3D"] = GpuSample(1_000 + firstDelta, 2_000),
            ["engine_b_engtype_3D"] = GpuSample(1_000 + secondDelta, 2_000),
        };

        Assert.Equal(expected, WindowsResourceUsageInspector.CalculateGpuUsage(before, after));
    }

    [Fact]
    public void GetSnapshot_PreCanceledTokenThrowsSynchronously()
    {
        var token = new CancellationToken(canceled: true);
        var inspector = new WindowsResourceUsageInspector();

        var exception = Assert.Throws<OperationCanceledException>(() => inspector.GetSnapshot(token));

        Assert.Equal(token, exception.CancellationToken);
    }

    [Fact]
    public void FiveMCpuUsage_UsesOnlyComparableProcessesAndTotalLogicalCapacity()
    {
        var existing = new FiveMProcessIdentity(42, 1000);
        var previous = new Dictionary<FiveMProcessIdentity, TimeSpan>
        {
            [existing] = TimeSpan.FromSeconds(2)
        };
        var current = new Dictionary<FiveMProcessIdentity, TimeSpan>
        {
            [existing] = TimeSpan.FromSeconds(2.2),
            [new FiveMProcessIdentity(84, 2000)] = TimeSpan.FromSeconds(4)
        };

        var usage = WindowsFiveMResourceUsageInspector.CalculateCpuPercent(
            previous,
            current,
            TimeSpan.FromSeconds(1),
            logicalProcessorCount: 4);

        Assert.NotNull(usage);
        Assert.Equal(5, usage.Value, precision: 6);
        Assert.Null(WindowsFiveMResourceUsageInspector.CalculateCpuPercent(
            previous,
            current,
            TimeSpan.FromMinutes(1),
            logicalProcessorCount: 4));
    }

    // Timer100Ns reports 100 * delta(rawValue) / delta(timeStamp100nSec).
    private static CounterSample GpuSample(long rawValue, long timestamp) => new(
        rawValue, 0, 10_000_000, 10_000_000, timestamp, timestamp,
        PerformanceCounterType.Timer100Ns, timestamp);

    /// <summary>
    /// Regressão do layout de <c>PDH_FMT_COUNTERVALUE</c>: sem o campo
    /// <c>CStatus</c>, a união do valor caía sobre ele e a leitura de CPU
    /// voltava exatamente 0 em qualquer máquina — o painel ao vivo mostrava
    /// 0% para sempre, sem nunca falhar de forma visível.
    /// </summary>
    [Fact]
    public async Task GetSnapshot_ReadsRealCpuUsageWhileTheMachineIsBusy()
    {
        using var busy = new CancellationTokenSource();
        var load = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount / 2))
            .Select(_ => Task.Run(
                () =>
                {
                    while (!busy.IsCancellationRequested)
                    {
                    }
                },
                busy.Token))
            .ToArray();

        try
        {
            var snapshot = new WindowsResourceUsageInspector().GetSnapshot(TestContext.Current.CancellationToken);

            // Onde o contador não existe, a leitura é honestamente indisponível;
            // o que não pode voltar é um zero constante com o PC ocupado.
            if (snapshot.CpuPercent is { } cpu)
            {
                Assert.InRange(cpu, 0.5, 100);
            }
        }
        finally
        {
            await busy.CancelAsync();
            await Task.WhenAll(load).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken: global::Xunit.TestContext.Current.CancellationToken);
        }
    }
}
