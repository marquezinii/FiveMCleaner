using System.Windows;

namespace Ralven.App.Services;

/// <summary>
/// Abre um processo/link/pasta externa com o mesmo tratamento de falha em
/// toda a interface: um verbo de shell sem handler padrão, política de grupo
/// bloqueando, ou pasta sem acesso nunca deve derrubar o app inteiro via
/// <see cref="AppDomain.UnhandledException"/> — só mostra um aviso local.
/// </summary>
public static class ExternalLauncher
{
    public static void TryOpen(Action launch)
    {
        try
        {
            launch();
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
            Ralven.App.Views.ErrorDialog.ShowRecoverable(
                owner,
                exception,
                owner is Ralven.App.MainWindow mainWindow ? mainWindow.OpenBugReport : null);
        }
    }
}
