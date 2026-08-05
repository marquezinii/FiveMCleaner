using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class ResourceUsageInspectorTests
{
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
            var snapshot = new WindowsResourceUsageInspector().GetSnapshot();

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
            await Task.WhenAll(load).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
