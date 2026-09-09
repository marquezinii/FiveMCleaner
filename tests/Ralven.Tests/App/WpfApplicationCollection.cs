using Xunit;

namespace Ralven.Tests.App;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfApplicationCollection
{
    public const string Name = "WpfApplication";
}
