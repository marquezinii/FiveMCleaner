using Xunit;

namespace Ralven.Tests.App;

/// <summary>
/// Regression coverage for the first-launch language pin: the programmatic
/// <c>LanguageSelector.SelectedIndex</c> sync in <c>MainWindow_Loaded</c> must
/// not be treated as a user choice. Otherwise the <c>SelectionChanged</c>
/// handler would call <c>SelectLanguage</c>, converting a stored
/// <c>Automatic</c> preference into the detected concrete language and
/// persisting the pin on startup.
/// </summary>
public sealed class MainWindowLanguageSelectorSyncTests
{
    [Fact]
    public void Loaded_ProgrammaticSyncIsGuardedSoItCannotPinTheLanguagePreference()
    {
        var source = ReadMainWindowSource();

        Assert.Contains("syncingLanguageSelector = true;", source, StringComparison.Ordinal);
        Assert.Contains("PopulateLanguageSelector(viewModel.LanguagePreference)", source, StringComparison.Ordinal);
        Assert.Contains("syncingLanguageSelector = false;", source, StringComparison.Ordinal);
        // O try/finally garante que o flag nunca fica preso em true, mesmo se
        // a atribuição do SelectedIndex lançar uma exceção.
        Assert.Contains("finally", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionChanged_IgnoresChangesRaisedByTheProgrammaticSync()
    {
        var source = ReadMainWindowSource();

        Assert.Contains(
            "if (syncingLanguageSelector || !IsLoaded",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProgrammaticSyncPreservesTheAutomaticPreference()
    {
        var source = ReadMainWindowSource();

        Assert.Contains("LocalizationCatalog.SupportedLanguages", source, StringComparison.Ordinal);
        Assert.Contains("language.CultureName.Equals(normalized", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppLanguagePreference.Spanish =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualCapture_ChangesLanguageThroughTheViewModel()
    {
        var root = TestHelpers.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Ralven.App", "MainWindow.Capture.xaml.cs"));

        Assert.Contains("viewModel.SelectLanguage(language)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalizationService.Current.SetLanguage(language)", source, StringComparison.Ordinal);
    }

    private static string ReadMainWindowSource() => TestHelpers.ReadMainWindowSource();
}
