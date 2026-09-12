using System.Globalization;
using Ralven.App.Services;
using Ralven.Contracts;
using Xunit;

namespace Ralven.Tests.App;

/// <summary>
/// <see cref="BugCodeCatalogTests"/> (in Ralven.Contracts, which has no resx
/// access) only proves that every category resolves to a non-null resource
/// KEY. <see cref="ILocalizationService.GetString"/> returns the key verbatim
/// on a miss instead of throwing, so a resx entry could go missing silently
/// while that test — and every other test in the suite — stays green. These
/// tests close that gap by asserting each key actually has a translated
/// value in every supported culture.
/// </summary>
public sealed class BugCodeCategoryLocalizationTests
{
    public static IEnumerable<object[]> SupportedCultures()
    {
        yield return [CultureInfo.GetCultureInfo("en-US")];
        yield return [CultureInfo.GetCultureInfo("pt-BR")];
        yield return [CultureInfo.GetCultureInfo("es")];
    }

    [Theory]
    [MemberData(nameof(SupportedCultures))]
    public void GetString_EveryCategoryResourceKey_ResolvesToATranslationNotTheRawKey(
        CultureInfo culture)
    {
        var localization = new LocalizationService(culture);

        var keys = BugCodeCatalog.CategoryResourceKeys.Values
            .Append("BugCode.Category.Unknown");

        foreach (var key in keys)
        {
            var value = localization.GetString(key);

            Assert.NotEqual(key, value);
        }
    }
}
