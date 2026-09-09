using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ralven.App.Controls;
using Ralven.App.Services;
using Xunit;

namespace Ralven.Tests.App;

public sealed class AnimatedRalvenLogoTests
{
    [Fact]
    public void Logo_CoalescesClicksReturnsToRestAndStopsWhenHiddenOrUnloaded()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources["LocalizedStrings"] = new LocalizedStrings();
                var logo = new AnimatedRalvenLogo { Width = 104, Height = 104 };
                window = new Window
                {
                    Content = logo, Width = 240, Height = 240, ShowInTaskbar = false, Style = null,
                    ShowActivated = false, Left = -10000, Top = -10000,
                };
                // Simulate focus without stealing the desktop or depending on other apps/parallel tests.
                var activeKey = (DependencyPropertyKey)typeof(Window)
                    .GetField("IsActivePropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                window.SetValue(activeKey, true);
                window.Show();
                Pump(TimeSpan.FromMilliseconds(100));
                var button = (Button)logo.FindName("LogoButton");
                var rotation = (RotateTransform)logo.FindName("LogoRotation");
                var scale = (ScaleTransform)logo.FindName("LogoScale");
                var timer = Field<DispatcherTimer>(logo, "idleTimer");
                var storyboard = Field<Storyboard>(logo, "spin");

                Assert.Equal(TimeSpan.FromSeconds(10), logo.IdleMinimum);
                Assert.Equal(TimeSpan.FromSeconds(20), logo.IdleMaximum);
                Assert.Throws<ArgumentException>(() => logo.IdleMinimum = TimeSpan.Zero);
                Assert.Throws<ArgumentException>(() => logo.IdleMaximum = TimeSpan.MaxValue);
                Assert.Equal(0, rotation.Angle);

                if (SystemParameters.ClientAreaAnimation)
                {
                    Assert.True(timer.IsEnabled);
                    Assert.InRange(timer.Interval.TotalSeconds, 10, 20);
                    for (var i = 0; i < 50; i++) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(Field<bool>(logo, "spinning"));
                    Assert.True(Field<bool>(logo, "replayRequested"));
                    Assert.False(timer.IsEnabled);
                    storyboard.SeekAlignedToLastTick(logo, TimeSpan.FromMilliseconds(360), TimeSeekOrigin.BeginTime);
                    Assert.InRange(rotation.Angle, 170, 190);
                    Assert.InRange(scale.ScaleX, 0.93, 0.96);
                    Pump(TimeSpan.FromMilliseconds(500));
                    Assert.True(Field<bool>(logo, "spinning"), $"Replay: active={window.IsActive}, queued={Field<bool>(logo, "replayRequested")}, timer={timer.IsEnabled}");
                    Assert.False(Field<bool>(logo, "replayRequested"));
                    Pump(TimeSpan.FromMilliseconds(800));
                    Assert.False(Field<bool>(logo, "spinning"));
                    Assert.Equal(0, rotation.Angle);
                    Assert.Equal(1, scale.ScaleX);
                    Assert.Equal(1, scale.ScaleY);
                    Assert.True(timer.IsEnabled);

                    logo.IdleMinimum = TimeSpan.FromMilliseconds(100);
                    logo.IdleMaximum = logo.IdleMinimum;
                    Pump(TimeSpan.FromMilliseconds(200));
                    Assert.True(Field<bool>(logo, "spinning"));
                    logo.Visibility = Visibility.Collapsed;
                    Assert.False(timer.IsEnabled);
                    Assert.False(Field<bool>(logo, "spinning"));
                    // Storyboard.Remove takes effect on the next animation clock tick.
                    Pump(TimeSpan.FromMilliseconds(50));
                    Assert.Equal(0, rotation.Angle);
                    logo.IdleMinimum = TimeSpan.FromSeconds(10);
                    logo.IdleMaximum = TimeSpan.FromSeconds(20);
                    logo.Visibility = Visibility.Visible;
                    Assert.True(timer.IsEnabled);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(Field<bool>(logo, "spinning"));
                    logo.IsEnabled = false;
                    Assert.False(Field<bool>(logo, "spinning"));
                    Assert.False(timer.IsEnabled);
                    logo.IsEnabled = true;
                }
                else
                {
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(timer.IsEnabled);
                    Assert.False(Field<bool>(logo, "spinning"));
                }

                window.Content = null;
                Pump(TimeSpan.FromMilliseconds(50));
                Assert.False(timer.IsEnabled);
                Assert.False(Field<bool>(logo, "subscribed"));
                Assert.False(Field<bool>(logo, "replayRequested"));
                window.Content = logo;
                Pump(TimeSpan.FromMilliseconds(50));
                Assert.Equal(SystemParameters.ClientAreaAnimation, timer.IsEnabled);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Logo animation check timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static T Field<T>(AnimatedRalvenLogo logo, string name) =>
        (T)typeof(AnimatedRalvenLogo).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logo)!;

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
