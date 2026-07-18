using FiveMCleaner.Windows.Actions;
using FiveMCleaner.Windows.Infrastructure;
using Xunit;

namespace FiveMCleaner.Tests.Windows;

public sealed class PowerCfgControllerTests
{
    [Fact]
    public async Task Controller_AlwaysUsesAbsoluteSystem32Executable()
    {
        var scheme = Guid.NewGuid();
        var runner = new CapturingRunner(scheme);
        var controller = new PowerCfgController(runner);

        var actual = await controller.GetActiveSchemeAsync(CancellationToken.None);

        Assert.Equal(scheme, actual);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "powercfg.exe")),
            runner.Executable);
        Assert.True(Path.IsPathFullyQualified(runner.Executable));
    }

    private sealed class CapturingRunner : ICommandRunner
    {
        private readonly Guid scheme;

        public CapturingRunner(Guid scheme)
        {
            this.scheme = scheme;
        }

        public string Executable { get; private set; } = string.Empty;

        public Task<CommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Executable = executable;
            return Task.FromResult(new CommandResult(
                0,
                $"Power Scheme GUID: {scheme:D} (Balanced)",
                string.Empty));
        }
    }
}
