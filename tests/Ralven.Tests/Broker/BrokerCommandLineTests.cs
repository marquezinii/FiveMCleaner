using Ralven.Broker;
using Xunit;

namespace Ralven.Tests.Broker;

public sealed class BrokerCommandLineTests
{
    [Fact]
    public void Parse_RequiresAndNormalizesCulture()
    {
        var pipeId = Guid.NewGuid();

        var command = BrokerCommandLine.Parse(
        [
            "--request", @"C:\Temp\plan.json",
            "--pipe", pipeId.ToString("D"),
            "--culture", "fr-fr"
        ]);

        Assert.Equal(BrokerOperation.ExecutePlan, command.Operation);
        Assert.Equal(pipeId, command.PipeId);
        Assert.Equal("fr-FR", command.CultureName);
    }

    [Fact]
    public void Parse_RejectsMissingOrInvalidCulture()
    {
        var pipeId = Guid.NewGuid().ToString("D");

        Assert.Throws<BrokerUsageException>(() => BrokerCommandLine.Parse(
            ["--request", @"C:\Temp\plan.json", "--pipe", pipeId]));
        Assert.Throws<BrokerUsageException>(() => BrokerCommandLine.Parse(
        [
            "--request", @"C:\Temp\plan.json",
            "--pipe", pipeId,
            "--culture", "not_a_culture"
        ]));
    }
}
