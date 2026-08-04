using FiveMCleaner.App.Controls;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// O gráfico ao vivo só é útil se o ponto sob o cursor for realmente o ponto
/// desenhado ali. Estes testes fixam esse mapeamento nos dois sentidos.
/// </summary>
public sealed class LivePerformanceChartTests
{
    [Fact]
    public void IndexAt_CountsBackwardsFromTheNewestSampleOnTheRightEdge()
    {
        // 60 pontos em 590 px: 10 px por amostra.
        Assert.Equal(0, LivePerformanceChart.IndexAt(590, 590, 60));
        Assert.Equal(1, LivePerformanceChart.IndexAt(580, 590, 60));
        Assert.Equal(59, LivePerformanceChart.IndexAt(0, 590, 60));
    }

    [Fact]
    public void IndexAt_FollowsTheSpacingOfAHistoryThatIsStillFilling()
    {
        // Com 10 amostras, elas ocupam a largura inteira: o passo é maior, e o
        // ponto sob o cursor precisa acompanhar esse mesmo espaçamento.
        Assert.Equal(150d, LivePerformanceChart.StepFor(600, 5));
        Assert.Equal(0, LivePerformanceChart.IndexAt(600, 600, 5));
        Assert.Equal(2, LivePerformanceChart.IndexAt(300, 600, 5));
        Assert.Equal(4, LivePerformanceChart.IndexAt(0, 600, 5));
    }

    [Fact]
    public void IndexAt_RejectsPositionsOutsideTheDrawnWindow()
    {
        Assert.Equal(-1, LivePerformanceChart.IndexAt(-20, 590, 60));
        Assert.Equal(-1, LivePerformanceChart.IndexAt(700, 590, 60));
        Assert.Equal(-1, LivePerformanceChart.IndexAt(4, 6, 60));
        // Uma única amostra ainda não forma uma janela navegável.
        Assert.Equal(-1, LivePerformanceChart.IndexAt(300, 590, 1));
    }

    [Fact]
    public void SampleAt_ReadsTheHistoryFromTheNewestValue()
    {
        double[] values = [10, 20, 30];

        Assert.Equal(30, LivePerformanceChart.SampleAt(values, 0));
        Assert.Equal(10, LivePerformanceChart.SampleAt(values, 2));
        // Janela ainda incompleta: não há amostra nessa posição, e nenhum valor
        // é inventado para preenchê-la.
        Assert.Null(LivePerformanceChart.SampleAt(values, 3));
        Assert.Null(LivePerformanceChart.SampleAt(null, 0));
    }

    [Fact]
    public void ValueToY_KeepsZeroOnTheFloorAndClampsOutOfRangeReadings()
    {
        Assert.Equal(200, LivePerformanceChart.ValueToY(0, 200));
        Assert.Equal(200, LivePerformanceChart.ValueToY(-5, 200));
        // 12 px de folga no topo para o traço de 100% não encostar na borda.
        Assert.Equal(12, LivePerformanceChart.ValueToY(100, 200));
        Assert.Equal(12, LivePerformanceChart.ValueToY(140, 200));
        Assert.Equal(106, LivePerformanceChart.ValueToY(50, 200));
    }
}
