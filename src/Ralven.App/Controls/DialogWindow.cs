using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Size = System.Windows.Size;
using Brush = System.Windows.Media.Brush;

namespace Ralven.App.Controls;

/// <summary>Owned modal surface. Windows owns modality; the shell owns presentation.</summary>
public class DialogWindow : Window
{
    public static readonly DependencyProperty CanDismissProperty = DependencyProperty.Register(
        nameof(CanDismiss), typeof(bool), typeof(DialogWindow), new PropertyMetadata(true));

    private AdornerLayer? backdropLayer;
    private DialogBackdrop? backdrop;
    private IInputElement? previousFocus;

    public DialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Ralven;component/Themes/Dialogs.xaml", UriKind.Relative)
        });
        SetResourceReference(StyleProperty, "DialogWindowStyle");
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,
            (_, _) => Close(), (_, e) => e.CanExecute = CanDismiss));
        Loaded += DialogLoaded;
    }

    public bool CanDismiss
    {
        get => (bool)GetValue(CanDismissProperty);
        set => SetValue(CanDismissProperty, value);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!CanDismiss && DialogResult != true) e.Cancel = true;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Header") is FrameworkElement header)
        {
            header.MouseLeftButtonDown += (_, e) =>
            {
                if (ResizeMode == ResizeMode.CanResize && e.OriginalSource is not Button)
                    DragMove();
            };
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(ResizeMode == ResizeMode.CanResize ? 6 : 0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            UseAeroCaptionButtons = false
        });
        ConstrainToMonitor();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ConstrainToMonitor();
    }

    private void ConstrainToMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var reference = !IsVisible && Owner is not null ? Owner : this;
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(reference).Handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(reference);
        var available = AvailableSize(new Size(area.Width, area.Height), dpi);
        MinWidth = Math.Min(MinWidth, available.Width);
        MinHeight = Math.Min(MinHeight, available.Height);
        MaxWidth = available.Width;
        MaxHeight = available.Height;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }

    internal static Size AvailableSize(Size pixels, DpiScale dpi) => new(
        Math.Max(1, pixels.Width / dpi.DpiScaleX - 32),
        Math.Max(1, pixels.Height / dpi.DpiScaleY - 32));

    protected void SetPreferredHeight(double height)
    {
        var previousHeight = Height;
        Height = Math.Min(height, MaxHeight);
        if (!IsVisible || previousHeight == Height) return;
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var minimumTop = Top + PointFromScreen(new System.Windows.Point(area.Left, area.Top)).Y + 16;
        var maximumTop = Top + PointFromScreen(new System.Windows.Point(area.Left, area.Bottom)).Y - Height - 16;
        Top = Math.Clamp(Top + (previousHeight - Height) / 2, minimumTop, Math.Max(minimumTop, maximumTop));
    }

    private void DialogLoaded(object sender, RoutedEventArgs e)
    {
        if (Owner?.Content is UIElement ownerContent)
        {
            previousFocus = FocusManager.GetFocusedElement(Owner);
            backdropLayer = AdornerLayer.GetAdornerLayer(ownerContent);
            if (backdropLayer is not null)
            {
                backdrop = new DialogBackdrop(ownerContent);
                backdropLayer.Add(backdrop);
            }
        }

        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast
            && GetTemplateChild("PART_Surface") is UIElement surface)
        {
            surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!IsVisible || Content is not UIElement content) return;
            // Keep explicit focus choices (e.g. a validation field or safe cancel action).
            if (!content.IsKeyboardFocusWithin)
                content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Let a combo/dropdown consume its first Escape before dismissing the dialog.
        if (e.Key == Key.Escape && !e.Handled && !HasOpenDropDown(this))
        {
            e.Handled = true;
            if (CanDismiss) Close();
        }
    }

    private static bool HasOpenDropDown(DependencyObject root)
    {
        if (root is ComboBox { IsDropDownOpen: true }) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (HasOpenDropDown(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        var owner = Owner;
        if (backdrop is not null) backdropLayer?.Remove(backdrop);
        base.OnClosed(e);
        // No delayed Close: DialogResult, cancellation and consent must stay synchronous.
        if (owner is { IsVisible: true })
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (previousFocus is UIElement { IsVisible: true, IsEnabled: true } element)
                    Keyboard.Focus(element);
            });
    }

    private sealed class DialogBackdrop : Adorner
    {
        private static readonly DependencyProperty ShadeProperty = DependencyProperty.Register(
            "Shade", typeof(Brush), typeof(DialogBackdrop),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public DialogBackdrop(UIElement owner) : base(owner)
        {
            IsHitTestVisible = false;
            SetResourceReference(ShadeProperty, "DialogBackdropBrush");
        }

        protected override void OnRender(DrawingContext drawingContext) =>
            drawingContext.DrawRectangle((Brush?)GetValue(ShadeProperty), null, new Rect(AdornedElement.RenderSize));
    }
}
