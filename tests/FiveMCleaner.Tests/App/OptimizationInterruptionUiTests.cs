using Xunit;

namespace FiveMCleaner.Tests.App;

public sealed class OptimizationInterruptionUiTests
{
    [Fact]
    public void MainWindow_ConfirmsBeforeCancellingOrClosingAnActiveOptimization()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "MainWindow.xaml.cs"));

        Assert.Contains("ConfirmOptimizationInterruption(closeApplication: false)", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmOptimizationInterruption(closeApplication: true)", source, StringComparison.Ordinal);
        Assert.Contains("closeAfterOptimizationStops = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelOptimization_Click(object sender, RoutedEventArgs e) =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OptimizerPlan_HidesImplementationChipsButPreservesActionDescriptions()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "MainWindow.xaml"));

        Assert.Contains("Text=\"{Binding Name}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Description}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding RiskLabel}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding PrivilegeLabel}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding PlanHeader}\"", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FiveMCleaner.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }
}
