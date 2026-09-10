using System.Diagnostics;
using System.Windows;
using Ralven.App.Services;
using Ralven.App.Views;
using Ralven.Contracts;

namespace Ralven.App;

public partial class MainWindow
{
    /// <summary>
    /// Duas regiões de Configurações: a categoria marcada decide qual painel
    /// de conteúdo aparece. Só um fica visível por vez; nenhuma outra lógica
    /// de navegação — a rolagem e a categoria são independentes da página
    /// selecionada na barra lateral.
    /// </summary>
    private async void SettingsCategory_Changed(object sender, RoutedEventArgs e)
    {
        AccountSettingsCard.Visibility = ReferenceEquals(sender, CategoryAccount) ? Visibility.Visible : Visibility.Collapsed;
        GeneralSettingsPanel.Visibility = ReferenceEquals(sender, CategoryGeneral) ? Visibility.Visible : Visibility.Collapsed;
        PrivacySettingsPanel.Visibility = ReferenceEquals(sender, CategoryPrivacy) ? Visibility.Visible : Visibility.Collapsed;
        ToolsSettingsPanel.Visibility = ReferenceEquals(sender, CategoryTools) ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = ReferenceEquals(sender, CategoryAbout) ? Visibility.Visible : Visibility.Collapsed;
        SettingsContentScrollViewer?.ScrollToTop();
        if (ReferenceEquals(sender, CategoryTools))
        {
            if (demoMode)
            {
                viewModel.ShowCacheDemoState();
            }
            else
            {
                await viewModel.RefreshCacheStorageAsync();
            }
        }
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

        ApplyLanguagePreference((item.Tag as string) switch
        {
            "pt-BR" => AppLanguagePreference.PortugueseBrazil,
            "en" => AppLanguagePreference.English,
            "es" => AppLanguagePreference.Spanish,
            _ => AppLanguagePreference.Automatic
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

    private void ApplyLanguagePreference(AppLanguagePreference preference)
    {
        if (IsLoaded)
        {
            viewModel.SelectLanguagePreference(preference);
            UpdateAccountButton();
        }
    }

    private void SyncGeneralSettingsControls()
    {
        syncingLanguageSelector = true;
        try
        {
            LanguageSelector.SelectedIndex = viewModel.LanguagePreference switch
            {
                AppLanguagePreference.PortugueseBrazil => 1,
                AppLanguagePreference.English => 2,
                AppLanguagePreference.Spanish => 3,
                _ => 0
            };
        }
        finally
        {
            syncingLanguageSelector = false;
        }

        ThemeSystemOption.IsChecked = viewModel.ThemePreference == AppThemePreference.System;
        ThemeDarkOption.IsChecked = viewModel.ThemePreference == AppThemePreference.Dark;
        ThemeLightOption.IsChecked = viewModel.ThemePreference == AppThemePreference.Light;
    }

    private async void RestoreGeneralDefaults_Click(object sender, RoutedEventArgs e)
    {
        var localization = LocalizationService.Current;
        var dialog = new OptimizationConfirmationWindow(
            localization.GetString("Settings.RestoreDefaults.Dialog.Title"),
            localization.GetString("Settings.RestoreDefaults.Dialog.Message"),
            localization.GetString("Settings.RestoreDefaults.Dialog.Cancel"),
            localization.GetString("Settings.RestoreDefaults.Dialog.Confirm"))
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await viewModel.RestoreGeneralSettingsDefaultsAsync();
        themeManager.Apply(viewModel.ThemePreference);
        SyncGeneralSettingsControls();
    }

    private async void RunGtaVBenchmark_Click(object sender, RoutedEventArgs e) => await viewModel.RunGtaVBenchmarkAsync();

    private async void CheckForUpdatesManually_Click(object sender, RoutedEventArgs e) => await viewModel.CheckForUpdatesManuallyAsync();

    private async void ClearRalvenCache_Click(object sender, RoutedEventArgs e)
    {
        var localization = LocalizationService.Current;
        if (!OptimizationConfirmationWindow.Confirm(
                this,
                localization.GetString("Settings.Cache.Confirm.Message"),
                localization.GetString("Settings.Cache.Confirm.Title"),
                localization.GetString("Settings.Cache.Button")))
        {
            return;
        }

        await viewModel.CleanRalvenCacheAsync();
    }

    private async void RetrySaveSettings_Click(object sender, RoutedEventArgs e) => await viewModel.RetrySaveSettingsAsync();

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

    private void OpenChangelog_Click(object sender, RoutedEventArgs e)
    {
        TryOpenExternal(() => Process.Start(new ProcessStartInfo
        {
            FileName = ProductIdentity.ReleasesUrl,
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
