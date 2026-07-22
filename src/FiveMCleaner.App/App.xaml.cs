using System.IO;
using System.Windows;
using System.Windows.Threading;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App;

public partial class App : System.Windows.Application
{
    private static int isHandlingFatalError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            ShowFatalError(exception);
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        WriteCrashLog(e.Exception);
        ShowFatalError(e.Exception);
        Current?.Shutdown(1);
    }

    private static void ShowFatalError(Exception exception)
    {
        if (Interlocked.Exchange(ref isHandlingFatalError, 1) != 0)
        {
            return;
        }

        try
        {
            System.Windows.MessageBox.Show(
                $"O FiveMCleaner encontrou um erro inesperado. Nenhuma nova alteração será aplicada.\n\n{exception.Message}",
                ProductIdentity.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Nunca transforme uma falha da caixa de diálogo em um loop do Dispatcher.
        }
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductIdentity.Name,
                "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}\n\n");
        }
        catch
        {
            // O log é diagnóstico opcional e não deve mascarar a exceção original.
        }
    }
}
