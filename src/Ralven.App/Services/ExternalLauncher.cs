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
            Ralven.App.Views.OptimizationConfirmationWindow.Inform(System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive),
                LocalizationService.Current.Format(
                    "Dialog.OpenExternal.Message",
                    LocalizationService.Current.DescribeException(exception)),
                LocalizationService.Current.GetString("Dialog.OpenExternal.Title"));
        }
    }
}
