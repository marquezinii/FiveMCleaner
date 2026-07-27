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

    [Fact]
    public async Task GetPciExpressAspmPolicyAsync_ParsesTheCurrentAcValueFromPowercfgQuery()
    {
        var runner = new ScriptedRunner(_ => new CommandResult(
            0,
            "Power Setting GUID: ee12f906-d277-404b-b6da-e5fa1a576df5  (Link State Power Management)\n"
                + "  Current AC Power Setting Index: 0x00000001\n"
                + "  Current DC Power Setting Index: 0x00000001\n",
            string.Empty));
        var controller = new PowerCfgController(runner);

        var value = await controller.GetPciExpressAspmPolicyAsync(CancellationToken.None);

        Assert.Equal(1, value);
    }

    [Fact]
    public async Task GetPciExpressAspmPolicyAsync_ReturnsNullWhenPowercfgFails()
    {
        var runner = new ScriptedRunner(_ => new CommandResult(1, string.Empty, "not found"));
        var controller = new PowerCfgController(runner);

        var value = await controller.GetPciExpressAspmPolicyAsync(CancellationToken.None);

        Assert.Null(value);
    }

    [Fact]
    public async Task GetPciExpressAspmPolicyAsync_ReturnsNullWhenOutputFormatIsUnrecognized()
    {
        var runner = new ScriptedRunner(_ => new CommandResult(0, "algo em outro idioma", string.Empty));
        var controller = new PowerCfgController(runner);

        var value = await controller.GetPciExpressAspmPolicyAsync(CancellationToken.None);

        Assert.Null(value);
    }

    [Fact]
    public async Task TrySetPciExpressAspmPolicyAsync_SetsBothAcAndDcThenAppliesTheScheme()
    {
        var calls = new List<IReadOnlyList<string>>();
        var runner = new ScriptedRunner(arguments =>
        {
            calls.Add(arguments);
            return new CommandResult(0, string.Empty, string.Empty);
        });
        var controller = new PowerCfgController(runner);

        var succeeded = await controller.TrySetPciExpressAspmPolicyAsync(0, CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal(3, calls.Count);
        Assert.Contains("/setacvalueindex", calls[0]);
        Assert.Contains("/setdcvalueindex", calls[1]);
        Assert.Contains("/S", calls[2]);
    }

    [Fact]
    public async Task TrySetPciExpressAspmPolicyAsync_ReturnsFalseWhenPowercfgFails()
    {
        var runner = new ScriptedRunner(_ => new CommandResult(1, string.Empty, "denied"));
        var controller = new PowerCfgController(runner);

        var succeeded = await controller.TrySetPciExpressAspmPolicyAsync(0, CancellationToken.None);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task TrySetPciExpressAspmPolicyAsync_RejectsOutOfRangeValues()
    {
        var runner = new ScriptedRunner(_ => new CommandResult(0, string.Empty, string.Empty));
        var controller = new PowerCfgController(runner);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => controller.TrySetPciExpressAspmPolicyAsync(3, CancellationToken.None));
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

    private sealed class ScriptedRunner : ICommandRunner
    {
        private readonly Func<IReadOnlyList<string>, CommandResult> respond;

        public ScriptedRunner(Func<IReadOnlyList<string>, CommandResult> respond)
        {
            this.respond = respond;
        }

        public Task<CommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(arguments));
        }
    }
}
