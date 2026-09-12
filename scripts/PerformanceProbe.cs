using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ralven.App;
using Ralven.App.Services;
using Ralven.App.ViewModels;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("--demo-synthetic") || args.Length < 2)
        {
            throw new ArgumentException("Usage: --demo-synthetic <report.json>");
        }

        // Load the real WPF resources and shell, with in-memory demo diagnostics.
        // Do not run App.OnStartup: the probe owns the dispatcher and lifetime.
        var app = new ProbeApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow();
        app.MainWindow = window;
        window.Show();
        window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var viewModel = (MainViewModel)window.DataContext;
                var samples = 0;
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.CpuUsageSeries)) samples++;
                };
                var results = new List<object>();
                await Task.Delay(TimeSpan.FromSeconds(12));
                if (app.Windows.OfType<MainWindow>().Count() != 1)
                {
                    throw new InvalidOperationException("The probe must measure exactly one main window.");
                }
                await Measure("foreground", () => window.Activate());
                var otherWindow = new Window { Style = new Style(typeof(Window)), Title = "Ralven performance probe", Width = 240, Height = 120 };
                await Measure("inactive", () => { otherWindow.Show(); otherWindow.Activate(); });
                otherWindow.Close();
                await Measure("minimized", () => window.WindowState = WindowState.Minimized);
                await Measure("tray", () => Invoke("HideToTray"));
                var root = Path.GetFullPath(Path.Combine("artifacts", "performance-fixture", "FiveM"));
                Directory.CreateDirectory(Path.Combine(root, "FiveM.app", "data"));
                var diagnosticField = typeof(MainViewModel).GetField("diagnostic", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var diagnostic = (AppDiagnostic)diagnosticField.GetValue(viewModel)!;
                diagnosticField.SetValue(viewModel, diagnostic with { FiveMRoot = root });
                viewModel.ToggleFiveMSessionMonitor();
                if (!viewModel.IsFiveMSessionMonitoring) throw new InvalidOperationException("Session monitor did not start.");
                await Measure("tray-session-monitor", () => { });
                await Measure("restored", window.RequestActivation);
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var capture = File.Create(Path.ChangeExtension(args[^1], ".png"));
                encoder.Save(capture);

                async Task Measure(string phase, Action transition)
                {
                    transition();
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    var foreground = phase is "foreground" or "restored";
                    bool WindowMatchesPhase() => phase switch
                    {
                        "foreground" or "restored" => window.IsVisible && window.IsActive && window.WindowState != WindowState.Minimized,
                        "inactive" => window.IsVisible && !window.IsActive && window.WindowState != WindowState.Minimized,
                        "minimized" => window.WindowState == WindowState.Minimized,
                        _ => !window.IsVisible
                    };
                    var windowStateStable = WindowMatchesPhase();
                    void TrackActivity(object? sender, EventArgs e) => windowStateStable &= WindowMatchesPhase();
                    void TrackVisibility(object sender, DependencyPropertyChangedEventArgs e) => windowStateStable &= WindowMatchesPhase();
                    window.Activated += TrackActivity;
                    window.Deactivated += TrackActivity;
                    window.StateChanged += TrackActivity;
                    window.IsVisibleChanged += TrackVisibility;
                    using var process = Process.GetCurrentProcess();
                    var cpu = process.TotalProcessorTime;
                    var allocations = GC.GetTotalAllocatedBytes();
                    var firstSample = samples;
                    var clock = Stopwatch.StartNew();
                    await Task.Delay(TimeSpan.FromSeconds(20));
                    window.Activated -= TrackActivity;
                    window.Deactivated -= TrackActivity;
                    window.StateChanged -= TrackActivity;
                    window.IsVisibleChanged -= TrackVisibility;
                    windowStateStable &= WindowMatchesPhase();
                    process.Refresh();
                    var metrics = (LiveSystemMetricsSnapshot?)typeof(MainViewModel)
                        .GetField("lastLiveMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel);
                    var result = new
                    {
                        phase,
                        windowStateStable,
                        collectionPolicySatisfied = viewModel.IsLiveMetricsActive == foreground
                            && (foreground ? samples > firstSample : samples == firstSample),
                        seconds = clock.Elapsed.TotalSeconds,
                        cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                        allocatedMiB = (GC.GetTotalAllocatedBytes() - allocations) / 1048576d,
                        privateMiB = process.PrivateMemorySize64 / 1048576d,
                        workingSetMiB = process.WorkingSet64 / 1048576d,
                        handles = process.HandleCount,
                        samples = samples - firstSample,
                        cpuPercent = metrics?.CpuPercent,
                        gpuPercent = metrics?.GpuPercent,
                        liveMetricsActive = viewModel.IsLiveMetricsActive,
                        windowActive = window.IsActive,
                        sessionMonitor = viewModel.IsFiveMSessionMonitoring
                    };
                    results.Add(result);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[^1]))!);
                    File.WriteAllText(args[^1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine(JsonSerializer.Serialize(result));
                    if (args.Contains("--verify"))
                    {
                        if (!result.windowStateStable || !result.collectionPolicySatisfied)
                            throw new InvalidOperationException($"Invalid window state or collection activity in {phase}; see validity fields in the report.");
                    }
                }

                void Invoke(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Environment.ExitCode = 1;
            }
            finally
            {
                typeof(MainWindow).GetField("allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                window.Close();
                app.Shutdown(Environment.ExitCode);
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
        });
        Dispatcher.Run();
    }

    private sealed class ProbeApplication : App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // The probe already constructed the shell; App.OnStartup would create a second one.
        }
    }
}
