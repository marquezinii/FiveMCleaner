using System.IO;
using System.Windows;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App;

public partial class App : System.Windows.Application
{
    private static int isHandlingFatalError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Exit += (_, _) => TryShutdownCrashReporting();

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            TryCaptureException(exception);
            ShowFatalError(exception);
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        WriteCrashLog(e.Exception);
        TryCaptureException(e.Exception);
        ShowFatalError(e.Exception);
        Current?.Shutdown(1);
    }

    /// <summary>
    /// Exceptions thrown on a background thread with no surrounding
    /// try/catch. The process is already terminating by the time this runs
    /// (<see cref="UnhandledExceptionEventArgs.IsTerminating"/> is true in
    /// practice for this case), so this only records the crash — it cannot
    /// show a dialog reliably from a thread that may not own a Dispatcher.
    /// </summary>
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog(exception);
            TryCaptureException(exception);
        }
    }

    /// <summary>
    /// A faulted <see cref="Task"/> was garbage-collected without anyone
    /// observing its exception. Not fatal in .NET (unlike classic .NET
    /// Framework), so this only records it and marks it observed.
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        TryCaptureException(e.Exception);
        e.SetObserved();
    }

    private static void TryCaptureException(Exception exception)
    {
        try
        {
            CrashReporting.Current.CaptureException(exception);
        }
        catch
        {
            // A crash-reporting failure must never mask the original crash
            // nor throw from inside a crash handler.
        }
    }

    private static void TryShutdownCrashReporting()
    {
        try
        {
            CrashReporting.Current.Shutdown();
        }
        catch
        {
            // Best-effort flush on exit; never block shutdown on it.
        }
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
                Services.LocalizationService.Current.Format("Dialog.FatalError.Message", exception.Message),
                ProductIdentity.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Never turn a dialog failure into a Dispatcher loop.
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
