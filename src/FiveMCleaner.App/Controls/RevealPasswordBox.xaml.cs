using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FiveMCleaner.App.Controls;

/// <summary>
/// Password field with a "show/hide" eye toggle.
///
/// WPF's <see cref="PasswordBox"/> deliberately exposes no way to render its
/// content in the clear, so revealing means swapping in a sibling
/// <see cref="TextBox"/>. Both live in the template and exactly one is
/// visible at a time; <see cref="Password"/> always reads from whichever is
/// currently active, so callers never have to know which mode is on.
///
/// The clear-text value only ever exists while the user has explicitly asked
/// to see it, and is never copied anywhere else -- there is no plaintext
/// backing field kept in sync behind the scenes.
/// </summary>
public partial class RevealPasswordBox : System.Windows.Controls.UserControl
{
    /// <summary>Hint shown while the field is empty.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(RevealPasswordBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(RevealPasswordBox), new PropertyMetadata(128, OnMaxLengthChanged));

    /// <summary>
    /// Guards the two-way copy between the masked and revealed fields: each
    /// one raises its own change event while being written to, which would
    /// otherwise re-enter <see cref="OnPasswordChanged"/> in a loop.
    /// </summary>
    private bool synchronizing;

    public RevealPasswordBox()
    {
        InitializeComponent();
        ApplyMaxLength(MaxLength);
    }

    /// <summary>Raised whenever the user changes the value, in either mode.</summary>
    public event EventHandler? PasswordChanged;

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>The current value, whether the field is masked or revealed.</summary>
    public string Password => RevealToggle.IsChecked == true ? Revealed.Text : Masked.Password;

    /// <summary>Clears the field and returns it to the masked state.</summary>
    public void Clear()
    {
        synchronizing = true;
        Masked.Clear();
        Revealed.Clear();
        synchronizing = false;
        RevealToggle.IsChecked = false;
        UpdatePlaceholder();
    }

    /// <summary>Moves keyboard focus into whichever input is currently shown.</summary>
    public new void Focus()
    {
        if (RevealToggle.IsChecked == true)
        {
            Revealed.Focus();
            Revealed.CaretIndex = Revealed.Text.Length;
        }
        else
        {
            Masked.Focus();
        }
    }

    private static void OnMaxLengthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((RevealPasswordBox)sender).ApplyMaxLength((int)args.NewValue);

    private void ApplyMaxLength(int maxLength)
    {
        Masked.MaxLength = maxLength;
        Revealed.MaxLength = maxLength;
    }

    private void Masked_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (synchronizing) return;
        OnPasswordChanged();
    }

    private void Revealed_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (synchronizing) return;
        OnPasswordChanged();
    }

    private void OnPasswordChanged()
    {
        UpdatePlaceholder();
        PasswordChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RevealToggle_Changed(object sender, RoutedEventArgs e)
    {
        var revealing = RevealToggle.IsChecked == true;

        synchronizing = true;
        if (revealing)
        {
            Revealed.Text = Masked.Password;
        }
        else
        {
            Masked.Password = Revealed.Text;
            Revealed.Clear();
        }
        synchronizing = false;

        Masked.Visibility = revealing ? Visibility.Collapsed : Visibility.Visible;
        Revealed.Visibility = revealing ? Visibility.Visible : Visibility.Collapsed;
        RevealIcon.Data = (Geometry)FindResource(revealing ? "IconEyeOff" : "IconEye");
        RevealToggle.ToolTip = revealing ? "Ocultar a senha" : "Mostrar a senha";

        // Keeps the caret where the user left it instead of dropping focus
        // to the toggle, so typing can continue uninterrupted.
        if (Masked.IsKeyboardFocusWithin || Revealed.IsKeyboardFocusWithin || RevealToggle.IsKeyboardFocused)
        {
            Focus();
        }

        UpdatePlaceholder();
    }

    private void RevealPasswordBox_Unloaded(object sender, RoutedEventArgs e) => Clear();

    private void UpdatePlaceholder() =>
        PlaceholderText.Visibility = Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Clicking anywhere on the field (not just the 12px-inset input) puts
    /// the caret in it, which is what a single bordered box looks like it
    /// should do.
    /// </summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.OriginalSource is DependencyObject source && IsInsideToggle(source)) return;
        if (!IsKeyboardFocusWithin) Focus();
    }

    private bool IsInsideToggle(DependencyObject source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, RevealToggle)) return true;
            if (ReferenceEquals(node, this)) return false;
        }
        return false;
    }
}
