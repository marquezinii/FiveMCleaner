using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ralven.App.Services;
using Ralven.Contracts;

namespace Ralven.App;

public partial class MainWindow
{
    private async Task CaptureIfRequestedAsync()
    {
        var arguments = Environment.GetCommandLineArgs();
        var argument = arguments
            .FirstOrDefault(value => value.StartsWith("--capture=", StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return;
        }

        try
        {
            var outputPath = Path.GetFullPath(argument["--capture=".Length..].Trim('"'));

            // O modo demo devolve AppSettings padrão de propósito (nunca lê
            // nem grava o arquivo do usuário), então o tema capturado sempre
            // seguia o do Windows — o tema claro era, na prática, impossível
            // de fotografar sem trocar a preferência do sistema inteiro da
            // pessoa. --capture-theme= resolve isso pelo mesmo caminho que
            // --capture-page= já usa para as páginas: só vale sob --capture=,
            // não persiste nada, e não existe fora do smoke-test.
            var theme = arguments
                .FirstOrDefault(value => value.StartsWith("--capture-theme=", StringComparison.OrdinalIgnoreCase));
            if (theme is not null)
            {
                var requested = theme["--capture-theme=".Length..].Trim('"');
                themeManager.Apply(requested.Equals("light", StringComparison.OrdinalIgnoreCase)
                    ? AppThemePreference.Light
                    : AppThemePreference.Dark);
            }

            var size = arguments
                .FirstOrDefault(value => value.StartsWith("--capture-size=", StringComparison.OrdinalIgnoreCase));
            if (size is not null
                && TryParseCaptureSize(size["--capture-size=".Length..].Trim('"'), out var width, out var height))
            {
                WindowState = WindowState.Normal;
                Width = Math.Max(MinWidth, width);
                Height = Math.Max(MinHeight, height);
                Left = 0;
                Top = 0;
            }

            // O smoke-test de captura sempre abriu na Visão geral. Com
            // --capture-page= ele consegue fotografar qualquer página, o que
            // é o único jeito de conferir o Otimizador sem interação manual.
            var page = arguments
                .FirstOrDefault(value => value.StartsWith("--capture-page=", StringComparison.OrdinalIgnoreCase));
            if (page is not null)
            {
                var tag = page["--capture-page=".Length..].Trim('"');
                var target = tag switch
                {
                    "System" => (Element: (UIElement)SystemPage, Nav: SystemNav),
                    "Applications" => (Element: (UIElement)ApplicationsPage, Nav: ApplicationsNav),
                    "Games" => (Element: (UIElement)GamesPage, Nav: GamesNav),
                    "Ultra" => ConfigureUltraCapture(true, arguments),
                    "UltraLocked" => ConfigureUltraCapture(false, arguments),
                    "Optimizer" => ConfigureOptimizerCapture(OptimizationScope.GeneralWindows, OptimizerNav),
                    "FiveMOptimizer" => ConfigureOptimizerCapture(OptimizationScope.FiveMLegacy, GamesNav),
                    "History" => (HistoryPage, HistoryNav),
                    "Settings" => ConfigureSettingsCapture(arguments),
                    _ => (DashboardPage, DashboardNav)
                };
                ActivateNavItem(target.Nav);
                Navigate(target.Element);
            }

            await Task.Delay(450);
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)),
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var stream = File.Create(outputPath);
            encoder.Save(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // O modo --capture= é um smoke-test: um caminho inválido ou um
            // disco cheio não pode transformar a captura em um crash da UI.
            // Sem o arquivo de saída, o script que orquestra o smoke-test
            // detecta a falha pelo resultado do processo.
        }
        finally
        {
            allowClose = true;
            trayIcon.Hide();
            // Capture mode is a one-shot smoke harness. Explicit shutdown is
            // required here because a headless WPF host may keep its dispatcher
            // alive after the window closes, making the release gate hang.
            System.Windows.Application.Current.Shutdown(0);
        }
    }

    private (UIElement Element, Wpf.Ui.Controls.NavigationViewItem Nav) ConfigureUltraCapture(bool pro, IReadOnlyList<string> arguments)
    {
        var target = ConfigureOptimizerCapture(OptimizationScope.GeneralWindows, OptimizerNav);
        if (demoMode) viewModel.SetProAccess(pro);
        viewModel.SelectUltra();
        var section = arguments.FirstOrDefault(value => value.StartsWith("--capture-ultra-section=", StringComparison.OrdinalIgnoreCase))?
            ["--capture-ultra-section=".Length..];
        var expander = section switch
        {
            "Profiles" => OptimizerPage.UltraWorkspace.RoutineExpander,
            "Tracking" => OptimizerPage.UltraWorkspace.TrackingExpander,
            "Measurements" => OptimizerPage.UltraWorkspace.MeasurementExpander,
            _ => null
        };
        if (expander is not null)
        {
            expander.IsExpanded = true;
            Dispatcher.InvokeAsync(expander.BringIntoView, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        return target;
    }

    internal static bool TryParseCaptureSize(string value, out int width, out int height)
    {
        width = 0;
        height = 0;
        var parts = value.Replace('X', 'x').Split('x', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && width is > 0 and <= 7680
            && height is > 0 and <= 4320;
    }

    private (UIElement Element, Wpf.Ui.Controls.NavigationViewItem Nav) ConfigureSettingsCapture(
        IReadOnlyList<string> arguments)
    {
        var categoryArgument = arguments.FirstOrDefault(value =>
            value.StartsWith("--capture-settings-category=", StringComparison.OrdinalIgnoreCase));
        var category = categoryArgument?["--capture-settings-category=".Length..].Trim('"');
        System.Windows.Controls.RadioButton selected = category switch
        {
            "Account" => CategoryAccount,
            "Privacy" => CategoryPrivacy,
            "Tools" => CategoryTools,
            "About" => CategoryAbout,
            _ => CategoryGeneral
        };
        selected.IsChecked = true;
        return (SettingsPage, SettingsNav);
    }

    private (UIElement Element, Wpf.Ui.Controls.NavigationViewItem Nav) ConfigureOptimizerCapture(
        OptimizationScope scope,
        Wpf.Ui.Controls.NavigationViewItem navigationItem)
    {
        viewModel.SetOptimizationScope(scope);
        return (OptimizerPage, navigationItem);
    }
}
