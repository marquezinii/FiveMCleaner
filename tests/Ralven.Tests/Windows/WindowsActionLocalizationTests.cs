using System.Globalization;
using Ralven.Windows.Actions;
using Xunit;

namespace Ralven.Tests.Windows;

public sealed class WindowsActionLocalizationTests
{
    [Theory]
    [InlineData("en-US", "1.5 GB")]
    [InlineData("pt-BR", "1,5 GB")]
    [InlineData("es-ES", "1,5 GB")]
    [InlineData("fr-FR", "1,5 Go")]
    public void Resources_FormatNumbersWithSelectedCulture(string cultureName, string expected)
    {
        var resolver = WindowsActionResources.ForCulture(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, resolver("Unit.Gigabytes", [1.5]));
    }
}
