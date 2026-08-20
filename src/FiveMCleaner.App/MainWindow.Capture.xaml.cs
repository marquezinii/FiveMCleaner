using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FiveMCleaner.App.Services;

namespace FiveMCleaner.App;

public partial class MainWindow
{
    private async Task CaptureIfRequestedAsync()
    {
        var argument = Environment.GetCommandLineArgs()
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
            var theme = Environment.GetCommandLineArgs()
                .FirstOrDefault(value => value.StartsWith("--capture-theme=", StringComparison.OrdinalIgnoreCase));
            if (theme is not null)
            {
                var requested = theme["--capture-theme=".Length..].Trim('"');
                themeManager.Apply(requested.Equals("light", StringComparison.OrdinalIgnoreCase)
                    ? AppThemePreference.Light
                    : AppThemePreference.Dark);
            }

            // O smoke-test de captura sempre abriu na Visão geral. Com
            // --capture-page= ele consegue fotografar qualquer página, o que
            // é o único jeito de conferir o Otimizador sem interação manual.
            var page = Environment.GetCommandLineArgs()
                .FirstOrDefault(value => value.StartsWith("--capture-page=", StringComparison.OrdinalIgnoreCase));
            if (page is not null)
            {
                var tag = page["--capture-page=".Length..].Trim('"');
                var target = tag switch
                {
                    "Optimizer" => (Element: (UIElement)OptimizerPage, Nav: OptimizerNav),
                    "History" => (HistoryPage, HistoryNav),
                    "Settings" => (SettingsPage, SettingsNav),
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
            Close();
        }
    }
}
