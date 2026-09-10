using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ralven.App;
using Ralven.App.Services;

// Compiled only with -p:RalvenStartupProbe=true. Both demo modes isolate persistence
// and remote services; --demo retains the real, read-only Windows diagnosis.
internal static class StartupProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        var modes = args.Where(value => value is "--demo" or "--demo-synthetic").ToArray();
        var reports = args.Where(value => !value.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (modes.Length != 1 || reports.Length != 1
            || args.Any(value => value.StartsWith("--", StringComparison.Ordinal)
                && value is not ("--demo" or "--demo-synthetic" or "--capture")))
        {
            Console.Error.WriteLine("Usage: Ralven.exe (--demo|--demo-synthetic) <report.json> [--capture]");
            return 1;
        }

        var reportPath = Path.GetFullPath(reports[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        using var process = Process.GetCurrentProcess();
        var clock = Stopwatch.StartNew();
        var processOffsetMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        var gate = new object();
        var stages = new List<Stage>();
        var finished = 0;
        var completionQueued = false;
        var startupReady = false;
        var mainRendered = false;
        var heartbeatCount = 0;
        var lastHeartbeatMs = 0d;
        var maximumDispatcherGapMs = 0d;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var heartbeat = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        App? app = null;

        void Observe(string stage)
        {
            bool finishOnInput;
            lock (gate)
            {
                stages.Add(new Stage(stage, processOffsetMs + clock.Elapsed.TotalMilliseconds,
                    process.TotalProcessorTime.TotalMilliseconds, GC.GetTotalAllocatedBytes(false)));
                startupReady |= stage == "startup-ready";
                mainRendered |= stage == "main-rendered";
                finishOnInput = !completionQueued && dispatcher.CheckAccess() && startupReady && mainRendered;
                completionQueued |= finishOnInput;
            }

            if (stage == "splash-rendered" && args.Contains("--capture") && app is not null)
            {
                var splash = typeof(App).GetField("startupSplash", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(app);
                if (splash is not null && splash.GetType().GetField("window", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(splash) is Window splashWindow)
                    Capture(splashWindow, Path.ChangeExtension(reportPath, ".splash.png"));
            }

            if (finishOnInput)
            {
                dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    Observe("ready-input-dispatched");
                    var valid = app?.MainWindow is MainWindow { IsVisible: true, IsLoaded: true };
                    Finish(valid ? "ready" : "invalid-ready-window", valid ? 0 : 1);
                    app?.Shutdown(valid ? 0 : 1);
                }));
            }
        }

        void Finish(string outcome, int exitCode)
        {
            if (Interlocked.Exchange(ref finished, 1) != 0) return;
            lock (gate)
            {
                maximumDispatcherGapMs = Math.Max(maximumDispatcherGapMs,
                    clock.Elapsed.TotalMilliseconds - lastHeartbeatMs);
                var report = new
                {
                    mode = modes[0], outcome, exitCode,
                    processOffsetMs,
                    elapsedMs = processOffsetMs + clock.Elapsed.TotalMilliseconds,
                    heartbeatIntervalMs = 16, heartbeatCount, maximumDispatcherGapMs,
                    stages = stages.ToArray()
                };
                File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }

            if (outcome == "ready" && args.Contains("--capture") && app?.MainWindow is MainWindow window)
            {
                Capture(window, Path.ChangeExtension(reportPath, ".png"));
            }
        }

        static void Capture(Window window, string path)
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var capture = File.Create(path);
            encoder.Save(capture);
        }

        heartbeat.Tick += (_, _) =>
        {
            lock (gate)
            {
                var now = clock.Elapsed.TotalMilliseconds;
                maximumDispatcherGapMs = Math.Max(maximumDispatcherGapMs, now - lastHeartbeatMs);
                lastHeartbeatMs = now;
                heartbeatCount++;
            }
        };
        heartbeat.Start();
        // A worker timer bounds even a blocked UI thread. Never touches real user
        // persistence: running this probe requires one of the isolated demo modes.
        using var timeout = new System.Threading.Timer(_ =>
        {
            Finish("timeout", 2);
            Environment.Exit(2);
        }, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);

        StartupTrace.Observer = Observe;
        try
        {
            StartupTrace.Mark("managed-entry");
            app = new App();
            app.InitializeComponent();
            StartupTrace.Mark("resources-ready");
            var result = app.Run();
            Finish("exited-before-ready", result == 0 ? 1 : result);
            return result == 0 && stages.Any(stage => stage.Name == "ready-input-dispatched") ? 0 : 1;
        }
        catch (Exception exception)
        {
            Finish("exception:" + exception.GetType().Name, 1);
            return 1;
        }
        finally
        {
            StartupTrace.Observer = null;
            heartbeat.Stop();
        }
    }

    private sealed record Stage(string Name, double ProcessMilliseconds, double CpuMilliseconds, long AllocatedBytes);
}
