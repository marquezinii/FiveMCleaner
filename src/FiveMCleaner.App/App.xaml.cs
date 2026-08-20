using System.IO;
using System.Windows;
using System.Windows.Threading;
using FiveMCleaner.App.Services;
using FiveMCleaner.Contracts;

namespace FiveMCleaner.App;

public partial class App : System.Windows.Application
{
    private static readonly string[] MotionDurationKeys =
    {
        "MotionMicro", "MotionControl", "MotionNav", "MotionEnter", "MotionStructural", "MotionExit"
    };

    private static int isHandlingFatalError;
    private SingleInstanceGuard? singleInstanceGuard;
    private Dictionary<string, Duration>? originalMotionDurations;

    /// <summary>
    /// Zera (ou restaura) as durações de <c>Themes/Tokens/Motion.xaml</c>
    /// conforme o Windows pede menos animação ou não. Storyboards declarados
    /// dentro de ControlTemplate são congelados e não conseguem consultar
    /// <see cref="MotionPolicy"/> em tempo de execução, então a política é
    /// aplicada na fonte: o token de duração. Assim todo controle do app —
    /// interruptor, segmentado, navegação — respeita a preferência de
    /// acessibilidade sem que cada template precise repetir a decisão.
    ///
    /// Roda no startup e de novo sempre que o Windows notifica uma mudança de
    /// <c>ClientAreaAnimation</c> (Efeitos de animação, em Configurações de
    /// Acessibilidade) — trocar a preferência com o app aberto tinha efeito
    /// só na próxima abertura, nunca na sessão corrente.
    /// </summary>
    private void ApplyMotionPolicyToDurationTokens()
    {
        originalMotionDurations ??= CaptureMotionDurations();

        var instant = new Duration(TimeSpan.Zero);
        foreach (var key in MotionDurationKeys)
        {
            if (!originalMotionDurations.TryGetValue(key, out var original))
            {
                continue;
            }

            Resources[key] = MotionPolicy.AnimationsEnabled ? original : instant;
        }
    }

    private Dictionary<string, Duration> CaptureMotionDurations()
    {
        var captured = new Dictionary<string, Duration>();
        foreach (var key in MotionDurationKeys)
        {
            if (Resources[key] is Duration duration)
            {
                captured[key] = duration;
            }
        }

        return captured;
    }

    private void OnSystemParametersChangedForMotion(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ApplyMotionPolicyToDurationTokens();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyMotionPolicyToDurationTokens();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChangedForMotion;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Exit += (_, _) => TryShutdownCrashReporting();

        // Demo mode (used for automated smoke tests/screenshots) is
        // intentionally exempt: it never persists settings or sends
        // telemetry either, and tooling may legitimately launch it
        // repeatedly in quick succession.
        var isDemoMode = e.Args.Any(value =>
            value.Equals("--demo", StringComparison.OrdinalIgnoreCase)
            || value.Equals("--demo-synthetic", StringComparison.OrdinalIgnoreCase));

        if (!isDemoMode)
        {
            singleInstanceGuard = new SingleInstanceGuard(AppEnvironment.Resolve());
            if (!singleInstanceGuard.TryAcquire())
            {
                // Another instance is already running: ask it to bring its
                // window to the foreground and shut down quietly, so a second
                // launch never stacks a duplicate process or tray icon.
                singleInstanceGuard.RequestActivation();
                singleInstanceGuard.Dispose();
                Shutdown(0);
                return;
            }

            singleInstanceGuard.ListenForActivation(OnActivationRequested);
            Exit += (_, _) => singleInstanceGuard.Dispose();
        }

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

    private static void OnActivationRequested()
    {
        // Raised on the SingleInstanceGuard listener thread; marshal to the
        // UI thread where the window lives.
        try
        {
            Current?.Dispatcher.BeginInvoke(ActivateMainWindowIfAvailable);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // The application is shutting down; there is no window to activate.
        }
    }

    private static void ActivateMainWindowIfAvailable()
    {
        if (Current?.MainWindow is MainWindow mainWindow)
        {
            mainWindow.RequestActivation();
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
                Services.LocalizationService.Current.Format(
                    "Dialog.FatalError.Message",
                    Services.LocalizationService.Current.DescribeException(exception)),
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
