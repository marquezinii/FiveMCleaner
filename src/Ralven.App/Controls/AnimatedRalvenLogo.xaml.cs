using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Controls;

public partial class AnimatedRalvenLogo : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(AnimatedRalvenLogo));

    public static readonly DependencyProperty IdleMinimumProperty = DependencyProperty.Register(
        nameof(IdleMinimum), typeof(TimeSpan), typeof(AnimatedRalvenLogo),
        new PropertyMetadata(TimeSpan.FromSeconds(10), OnIntervalChanged), IsValidInterval);

    public static readonly DependencyProperty IdleMaximumProperty = DependencyProperty.Register(
        nameof(IdleMaximum), typeof(TimeSpan), typeof(AnimatedRalvenLogo),
        new PropertyMetadata(TimeSpan.FromSeconds(20), OnIntervalChanged), IsValidInterval);

    private readonly DispatcherTimer idleTimer = new(DispatcherPriority.Background);
    private readonly Storyboard spin;
    private Window? owner;
    private bool subscribed;
    private bool spinning;
    private bool replayRequested;

    public AnimatedRalvenLogo()
    {
        InitializeComponent();
        SetResourceReference(SourceProperty, "RalvenAiLogoImage");
        spin = ((Storyboard)Resources["Spin"]).Clone();
        spin.Completed += Spin_Completed;
        idleTimer.Tick += (_, _) => StartSpin();
        Loaded += Logo_Loaded;
        Unloaded += Logo_Unloaded;
        IsVisibleChanged += (_, _) => RefreshActivity();
        IsEnabledChanged += (_, _) => RefreshActivity();
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public TimeSpan IdleMinimum
    {
        get => (TimeSpan)GetValue(IdleMinimumProperty);
        set => SetValue(IdleMinimumProperty, value);
    }

    public TimeSpan IdleMaximum
    {
        get => (TimeSpan)GetValue(IdleMaximumProperty);
        set => SetValue(IdleMaximumProperty, value);
    }

    private bool CanAnimate => IsLoaded && IsVisible && IsEnabled && SystemParameters.ClientAreaAnimation
        && owner is { IsActive: true, WindowState: not WindowState.Minimized };

    private static bool IsValidInterval(object value) =>
        value is TimeSpan interval && interval > TimeSpan.Zero && interval.TotalMilliseconds <= int.MaxValue;

    private static void OnIntervalChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((AnimatedRalvenLogo)sender).ScheduleIdle();

    private void Logo_Loaded(object sender, RoutedEventArgs e)
    {
        if (subscribed) return;
        subscribed = true;
        owner = Window.GetWindow(this);
        if (owner is not null)
        {
            owner.Activated += Owner_ActivityChanged;
            owner.Deactivated += Owner_ActivityChanged;
            owner.StateChanged += Owner_ActivityChanged;
        }
        SystemParameters.StaticPropertyChanged += SystemParameters_Changed;
        RefreshActivity();
    }

    private void Logo_Unloaded(object sender, RoutedEventArgs e)
    {
        if (owner is not null)
        {
            owner.Activated -= Owner_ActivityChanged;
            owner.Deactivated -= Owner_ActivityChanged;
            owner.StateChanged -= Owner_ActivityChanged;
        }
        SystemParameters.StaticPropertyChanged -= SystemParameters_Changed;
        owner = null;
        subscribed = false;
        StopAnimation();
    }

    private void Owner_ActivityChanged(object? sender, EventArgs e) => RefreshActivity();

    private void SystemParameters_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            Dispatcher.InvokeAsync(RefreshActivity);
    }

    private void RefreshActivity()
    {
        if (CanAnimate) ScheduleIdle();
        else StopAnimation();
    }

    private void ScheduleIdle()
    {
        idleTimer.Stop();
        if (!CanAnimate || spinning) return;
        // Clamp the upper bound while XAML/bindings update the two properties independently.
        var minimum = IdleMinimum.TotalMilliseconds;
        var maximum = Math.Max(minimum, IdleMaximum.TotalMilliseconds);
        idleTimer.Interval = TimeSpan.FromMilliseconds(minimum + Random.Shared.NextDouble() * (maximum - minimum));
        idleTimer.Start();
    }

    private void Logo_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAnimate) return;
        // Coalesce bursts into one extra turn: no overlapping clocks or unbounded click queue.
        if (spinning) replayRequested = true;
        else StartSpin();
    }

    private void StartSpin()
    {
        idleTimer.Stop();
        if (!CanAnimate || spinning) return;
        spinning = true;
        spin.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void Spin_Completed(object? sender, EventArgs e)
    {
        spin.Remove(this);
        spinning = false;
        if (replayRequested)
        {
            replayRequested = false;
            StartSpin();
        }
        else ScheduleIdle();
    }

    private void StopAnimation()
    {
        idleTimer.Stop();
        if (spinning) spin.Remove(this);
        spinning = false;
        replayRequested = false;
    }
}
