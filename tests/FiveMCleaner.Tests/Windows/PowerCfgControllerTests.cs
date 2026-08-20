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
        var runner = new StubRunner(_ => new CommandResult(
            0,
            $"Power Scheme GUID: {scheme:D} (Balanced)",
            string.Empty));
        var controller = new PowerCfgController(runner);

        var actual = await controller.GetActiveSchemeAsync(CancellationToken.None);

        Assert.Equal(scheme, actual);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "powercfg.exe")),
            runner.Executable);
        Assert.True(Path.IsPathFullyQualified(runner.Executable));
    }

    [Fact]
    public async Task GetPciExpressAspmPolicyAsync_ReturnsNullWhenActiveSchemeLookupFails()
    {
        var runner = new StubRunner(_ => new CommandResult(1, string.Empty, "error"));
        var controller = new PowerCfgController(runner);

        var value = await controller.GetPciExpressAspmPolicyAsync(CancellationToken.None);

        Assert.Null(value);
    }

    [Fact]
    public async Task GetPciExpressAspmPolicyAsync_ReturnsNullOrValidValueFromNativeApi()
    {
        var scheme = Guid.NewGuid();
        var runner = new StubRunner(_ => new CommandResult(
            0,
            $"Power Scheme GUID: {scheme:D} (Balanced)",
            string.Empty));
        var controller = new PowerCfgController(runner);

        var value = await controller.GetPciExpressAspmPolicyAsync(CancellationToken.None);

        if (value.HasValue)
        {
            Assert.InRange(value.Value, 0, 2);
        }
    }

    [Fact]
    public async Task TrySetPciExpressAspmPolicyAsync_SetsBothAcAndDcThenAppliesTheScheme()
    {
        var calls = new List<IReadOnlyList<string>>();
        var runner = new StubRunner(arguments =>
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
        var runner = new StubRunner(_ => new CommandResult(1, string.Empty, "denied"));
        var controller = new PowerCfgController(runner);

        var succeeded = await controller.TrySetPciExpressAspmPolicyAsync(0, CancellationToken.None);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task TrySetPciExpressAspmPolicyAsync_RejectsOutOfRangeValues()
    {
        var runner = new StubRunner(_ => new CommandResult(0, string.Empty, string.Empty));
        var controller = new PowerCfgController(runner);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => controller.TrySetPciExpressAspmPolicyAsync(3, CancellationToken.None));
    }

    private sealed class StubRunner(
        Func<IReadOnlyList<string>, CommandResult> respond) : ICommandRunner
    {
        public string Executable { get; private set; } = string.Empty;

        public Task<CommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Executable = executable;
            return Task.FromResult(respond(arguments));
        }
    }
}
