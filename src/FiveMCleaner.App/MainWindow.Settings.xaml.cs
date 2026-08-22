using System.Diagnostics;
using System.Windows;
using FiveMCleaner.App.Services;
using FiveMCleaner.App.Views;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App;

public partial class MainWindow
{
    /// <summary>
    /// Duas regiões de Configurações: a categoria marcada decide qual painel
    /// de conteúdo aparece. Só um fica visível por vez; nenhuma outra lógica
    /// de navegação — a rolagem e a categoria são independentes da página
    /// selecionada na barra lateral.
    /// </summary>
    private void SettingsCategory_Changed(object sender, RoutedEventArgs e)
    {
        AccountSettingsCard.Visibility = ReferenceEquals(sender, CategoryAccount) ? Visibility.Visible : Visibility.Collapsed;
        GeneralSettingsPanel.Visibility = ReferenceEquals(sender, CategoryGeneral) ? Visibility.Visible : Visibility.Collapsed;
        PrivacySettingsPanel.Visibility = ReferenceEquals(sender, CategoryPrivacy) ? Visibility.Visible : Visibility.Collapsed;
        ToolsSettingsPanel.Visibility = ReferenceEquals(sender, CategoryTools) ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = ReferenceEquals(sender, CategoryAbout) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SystemTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.System);

    private void DarkTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Dark);

    private void LightTheme_Checked(object sender, RoutedEventArgs e) => ApplyTheme(AppThemePreference.Light);

    private void LanguageSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (syncingLanguageSelector || !IsLoaded || LanguageSelector.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        ApplyLanguage((item.Tag as string) switch
        {
            "pt-BR" => AppLanguage.PortugueseBrazil,
            "es" => AppLanguage.Spanish,
            _ => AppLanguage.English
        });
    }

    private void ApplyTheme(AppThemePreference preference)
    {
        if (!IsLoaded)
        {
            return;
        }

        viewModel.SelectTheme(preference);
        themeManager.Apply(preference);
    }

    private void ApplyLanguage(AppLanguage language)
    {
        if (IsLoaded)
        {
            viewModel.SelectLanguage(language);
            UpdateAccountButton();
        }
    }

    private async void RunGtaVBenchmark_Click(object sender, RoutedEventArgs e) => await viewModel.RunGtaVBenchmarkAsync();

    private async void CheckForUpdatesManually_Click(object sender, RoutedEventArgs e) => await viewModel.CheckForUpdatesManuallyAsync();

    private void ReportBug_Click(object sender, RoutedEventArgs e)
    {
        IBugReportService bugReportService = TryCreateHttpsEndpoint(remoteServicesOptions.BugReportEndpoint, out var bugReportEndpoint)
            ? new CloudflareBugReportService(bugReportEndpoint, remoteServicesOptions.Environment)
            : new DisabledBugReportService();

        var dialog = new BugReportWindow(
            bugReportService,
            viewModel.AppVersion,
            viewModel.SelectedProfileName,
            viewModel.EditionBadgeLabel)
        {
            Owner = this
        };
        _ = dialog.ShowDialog();
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExternal(() => Process.Start(new ProcessStartInfo
        {
            FileName = ProductIdentity.RepositoryUrl,
            UseShellExecute = true
        }));
    }

    private void Discord_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExternal(() => Process.Start(new ProcessStartInfo
        {
            FileName = ProductIdentity.DiscordInviteUrl,
            UseShellExecute = true
        }));
    }
}
