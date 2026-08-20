using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace FiveMCleaner.Tests.App;

/// <summary>
/// Contrato dos dicionários de cor do redesign "prancheta técnica".
///
/// <para>
/// O <c>ThemeManager</c> troca <c>Colors.Dark.xaml</c> por <c>Colors.Light.xaml</c>
/// inteiro. Uma chave que exista em apenas um dos dois não falha na compilação:
/// ela falha em tempo de execução, no tema em que estiver faltando, e só aparece
/// para quem usar aquele tema. Estes testes travam a paridade e o contraste
/// mínimo de leitura dos pares realmente usados na interface.
/// </para>
/// </summary>
public sealed class ThemeTokenContractTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Pares (frente, fundo) que a interface realmente compõe, com a razão de
    /// contraste mínima exigida. Texto usa o piso 4.5:1 do WCAG AA porque as
    /// escalas menores do app (Overline/Caption, 11–12px) não se qualificam
    /// como "texto grande"; elementos não textuais usam pisos menores.
    /// </summary>
    public static TheoryData<string, string, string, double> ContrastPairs()
    {
        var data = new TheoryData<string, string, string, double>();
        foreach (var theme in new[] { "Colors.Dark.xaml", "Colors.Light.xaml" })
        {
            data.Add(theme, "TextPrimaryBrush", "Surface1Color", 4.5);
            data.Add(theme, "TextSecondaryBrush", "Surface1Color", 4.5);
            data.Add(theme, "TextTertiaryBrush", "Surface1Color", 4.5);
            data.Add(theme, "TextPrimaryBrush", "Surface2Color", 4.5);
            data.Add(theme, "TextSecondaryBrush", "Surface2Color", 4.5);
            data.Add(theme, "TextTertiaryBrush", "Surface2Color", 4.5);
            // Cabeçalho de tabela e coluna de notas ficam sobre o poço.
            data.Add(theme, "TextTertiaryBrush", "CanvasSunkenColor", 4.5);
            data.Add(theme, "AccentTextBrush", "Surface1Color", 4.5);
            data.Add(theme, "AccentTextBrush", "Surface2Color", 4.5);
            data.Add(theme, "SuccessBaseBrush", "Surface1Color", 4.5);
            data.Add(theme, "WarningBaseBrush", "Surface1Color", 4.5);
            data.Add(theme, "DangerBaseBrush", "Surface1Color", 4.5);
            data.Add(theme, "InfoBaseBrush", "Surface1Color", 4.5);
            data.Add(theme, "RevertBaseBrush", "Surface1Color", 4.5);
            // Texto sobre o botão primário preenchido, em repouso e em hover
            // (PrimaryButtonStyle troca o fundo para AccentBrightBrush sob o
            // ponteiro — o estado em que o usuário está olhando para o botão
            // não pode ser o único que fica ilegível).
            data.Add(theme, "TextOnAccentBrush", "AccentBrush", 4.5);
            data.Add(theme, "TextOnAccentBrush", "AccentBrightBrush", 4.5);
            // Preenchimento do acento contra a folha: componente não textual.
            data.Add(theme, "AccentBrush", "Surface1Color", 3.0);
        }

        return data;
    }

    [Fact]
    public void ThemeDictionaries_DefineTheSameKeys()
    {
        var dark = ReadKeys("Colors.Dark.xaml");
        var light = ReadKeys("Colors.Light.xaml");

        Assert.Equal(dark, light);
    }

    [Theory]
    [MemberData(nameof(ContrastPairs))]
    public void ThemeDictionaries_MeetMinimumContrast(string theme, string foreground, string background, double minimumRatio)
    {
        var colors = ReadColors(theme);

        Assert.True(colors.ContainsKey(foreground), $"{theme}: chave de frente ausente: {foreground}");
        Assert.True(colors.ContainsKey(background), $"{theme}: chave de fundo ausente: {background}");

        var ratio = ContrastRatio(colors[foreground], colors[background]);
        Assert.True(
            ratio >= minimumRatio,
            $"{theme}: {foreground} sobre {background} tem contraste {ratio:F2}:1, abaixo do mínimo {minimumRatio:F1}:1.");
    }

    [Fact]
    public void BrandInk_StaysOutOfTheInterfacePalette()
    {
        // O laranja da marca continua sendo compromisso de marca, mas o
        // redesign o restringiu ao logotipo na barra de título: ele era a
        // principal fonte de fadiga visual quando preenchia a interface.
        // Só BrandInkBrush pode carregá-lo, e só MainWindow.xaml pode usá-lo.
        var root = TestHelpers.FindRepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "FiveMCleaner.App");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var isTokenDictionary = Path.GetFileName(file).StartsWith("Colors.", StringComparison.Ordinal);
            var isShell = Path.GetFileName(file).Equals("MainWindow.xaml", StringComparison.Ordinal);
            var text = File.ReadAllText(file);

            if (!isTokenDictionary && text.Contains("BrandInkBrush", StringComparison.Ordinal) && !isShell)
            {
                offenders.Add($"{Path.GetFileName(file)} usa BrandInkBrush fora da barra de título");
            }
        }

        Assert.Empty(offenders);
    }

    private static SortedSet<string> ReadKeys(string fileName)
    {
        return new SortedSet<string>(ReadEntries(fileName).Select(entry => entry.Key), StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadColors(string fileName)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, element) in ReadEntries(fileName))
        {
            var candidate = element.Name.LocalName switch
            {
                "SolidColorBrush" => element.Attribute("Color")?.Value,
                "Color" => element.Value.Trim(),
                _ => null
            };

            if (candidate is not null && IsOpaqueHex(candidate))
            {
                colors[key] = candidate;
            }
        }

        return colors;
    }

    private static IEnumerable<(string Key, XElement Element)> ReadEntries(string fileName)
    {
        var path = Path.Combine(
            TestHelpers.FindRepositoryRoot(),
            "src",
            "FiveMCleaner.App",
            "Themes",
            "Tokens",
            fileName);

        return XDocument.Load(path).Root!
            .Elements()
            .Select(element => (Key: element.Attribute(XamlNamespace + "Key")?.Value, Element: element))
            .Where(entry => entry.Key is not null)
            .Select(entry => (entry.Key!, entry.Element));
    }

    private static bool IsOpaqueHex(string value)
    {
        return value.Length == 7
            && value[0] == '#'
            && value.AsSpan(1).ToString().All(Uri.IsHexDigit);
    }

    private static double ContrastRatio(string foreground, string background)
    {
        var a = RelativeLuminance(foreground);
        var b = RelativeLuminance(background);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        static double Channel(string hex, int offset)
        {
            var raw = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return raw <= 0.03928 ? raw / 12.92 : Math.Pow((raw + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(hex, 1)
            + 0.7152 * Channel(hex, 3)
            + 0.0722 * Channel(hex, 5);
    }
}
