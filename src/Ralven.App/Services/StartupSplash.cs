using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;

namespace Ralven.App.Services;

// An independent dispatcher keeps the real-work indicator responsive while WPF
// builds the main shell. No application resources cross the dispatcher boundary.
internal sealed class StartupSplash : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Dispatcher? dispatcher;
    private Window? window;
    private nint owner;
    private int disposeRequested;
    private bool terminated;
    private volatile bool wasShown;
    internal bool WasShown => wasShown;
    internal Task Completion => completion.Task;

    internal StartupSplash(string label, string closeLabel, bool lightTheme, Action cancel,
        TimeSpan? displayThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        var threshold = displayThreshold ?? TimeSpan.FromMilliseconds(180);
        if (threshold < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(displayThreshold));
        var thread = new Thread(() => Run(label, closeLabel, lightTheme, cancel, threshold))
        {
            IsBackground = true,
            Name = "Ralven startup presentation"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal void SetOwner(nint handle)
    {
        lock (gate)
        {
            if (Volatile.Read(ref disposeRequested) != 0) return;
            owner = handle;
            if (dispatcher is { HasShutdownStarted: false } current)
                current.BeginInvoke(() =>
                {
                    if (window is not null && Volatile.Read(ref disposeRequested) == 0)
                        new WindowInteropHelper(window).Owner = handle;
                });
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeRequested, 1) != 0) return;
        lock (gate)
        {
            if (terminated) return;
            stop.Cancel();
            if (dispatcher is { HasShutdownStarted: false } current)
                current.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    window?.Close();
                    current.BeginInvokeShutdown(DispatcherPriority.Send);
                }));
        }
    }

    private void Run(string label, string closeLabel, bool lightTheme, Action cancel, TimeSpan threshold)
    {
        Exception? failure = null;
        try
        {
            if (stop.Token.WaitHandle.WaitOne(threshold)) return;
            lock (gate) dispatcher = Dispatcher.CurrentDispatcher;
            var colors = new ResourceDictionary
            {
                Source = new Uri($"/Ralven;component/Themes/Tokens/Colors.{(lightTheme ? "Light" : "Dark")}.xaml", UriKind.Relative)
            };
            Brush Brush(string name) => (Brush)colors[name];
            var foreground = Brush("TextPrimaryBrush");
            window = new Window
            {
                Style = new Style(typeof(Window)),
                Width = Math.Min(640, SystemParameters.WorkArea.Width),
                Height = Math.Min(400, SystemParameters.WorkArea.Height),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, Title = "Ralven", Background = Brush("CanvasBaseBrush"),
                Foreground = foreground, FontFamily = new FontFamily("Segoe UI"),
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            window.Resources.MergedDictionaries.Add(colors);
            var root = new Grid { Margin = new Thickness(28), Style = new Style(typeof(Grid)) };
            var close = new Button
            {
                Style = new Style(typeof(Button)), Content = "×", ToolTip = closeLabel,
                Width = 36, Height = 36, FontSize = 24, Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent, Foreground = foreground, BorderThickness = new Thickness(0)
            };
            AutomationProperties.SetName(close, closeLabel);
            close.Click += (_, _) => window.Close();
            var content = new StackPanel
            {
                Style = new Style(typeof(StackPanel)), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var logo = new BitmapImage();
            logo.BeginInit();
            logo.UriSource = new Uri("pack://application:,,,/Ralven;component/Assets/Ralven.png");
            logo.DecodePixelWidth = 192;
            logo.CacheOption = BitmapCacheOption.OnLoad;
            logo.EndInit();
            logo.Freeze();
            content.Children.Add(new Image { Style = new Style(typeof(Image)), Source = logo, Width = 96, Height = 96 });
            content.Children.Add(new TextBlock
            {
                Style = new Style(typeof(TextBlock)), Text = label, Foreground = Brush("TextSecondaryBrush"),
                FontSize = 16, Margin = new Thickness(0, 28, 0, 20), HorizontalAlignment = HorizontalAlignment.Center
            });
            var dots = new StackPanel
            {
                Style = new Style(typeof(StackPanel)), Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            for (var index = 0; index < 3; index++)
            {
                var dot = new Ellipse
                {
                    Style = new Style(typeof(Ellipse)), Width = 6, Height = 6,
                    Margin = new Thickness(5, 0, 5, 0), Fill = foreground, Opacity = .35
                };
                if (MotionPolicy.AnimationsEnabled)
                    dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(500))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(index * 140), AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                dots.Children.Add(dot);
            }
            content.Children.Add(dots);
            root.Children.Add(content);
            root.Children.Add(close);
            window.Content = root;
            window.ContentRendered += (_, _) => StartupTrace.Mark("splash-rendered");
            window.Closed += (_, _) =>
            {
                if (Volatile.Read(ref disposeRequested) == 0) cancel();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            };
            nint ownerHandle;
            lock (gate) ownerHandle = owner;
            // Assigning an HWND owner can send synchronous window messages. Never
            // hold the lifecycle gate while Windows calls the main UI thread.
            if (ownerHandle != 0) new WindowInteropHelper(window).Owner = ownerHandle;
            StartupTrace.Mark("splash-prepared");
            if (stop.IsCancellationRequested) return;
            window.Show();
            wasShown = true;
            DispatcherTimer? heartbeat = null;
            if (StartupTrace.Observer is not null)
            {
                heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
                heartbeat.Tick += (_, _) => StartupTrace.Mark("splash-heartbeat");
                heartbeat.Start();
            }
            Dispatcher.Run();
            heartbeat?.Stop();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            Interlocked.Exchange(ref disposeRequested, 1);
            try
            {
                window?.Close();
                if (dispatcher is { HasShutdownStarted: false }) dispatcher.InvokeShutdown();
            }
            catch (Exception exception) { failure ??= exception; }
            lock (gate)
            {
                terminated = true;
                stop.Dispose();
            }
            if (failure is null) completion.TrySetResult();
            else completion.TrySetException(failure);
        }
    }
}
