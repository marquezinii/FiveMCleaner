using Xunit;

namespace FiveMCleaner.Tests.App;

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
        Assert.Contains("LanguageSelector.SelectedIndex = viewModel.IsPortugueseSelected", source, StringComparison.Ordinal);
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
    public void ProgrammaticSyncUsesCurrentLanguageNotAPinnedPreference()
    {
        var source = ReadMainWindowSource();

        // O índice sincronizado reflete o idioma já resolvido pela
        // preferência (inclusive o detectado em modo Automatic), e não chama
        // SelectLanguage de volta para a ViewModel.
        Assert.Contains("viewModel.IsPortugueseSelected", source, StringComparison.Ordinal);
        Assert.Contains("viewModel.IsSpanishSelected", source, StringComparison.Ordinal);
        Assert.Contains("viewModel.SelectLanguage", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "LanguageSelector.SelectedIndex = viewModel.LanguagePreference switch",
            source,
            StringComparison.Ordinal);
    }

    private static string ReadMainWindowSource()
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            root,
            "src",
            "FiveMCleaner.App",
            "MainWindow.xaml.cs"));
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
