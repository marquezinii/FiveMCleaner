using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FiveMCleaner.App.Services;
using UserControl = System.Windows.Controls.UserControl;
using TextBlock = System.Windows.Controls.TextBlock;
using Style = System.Windows.Style;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using Key = System.Windows.Input.Key;
using Brush = System.Windows.Media.Brush;

namespace FiveMCleaner.App.Controls;

/// <summary>
/// Seletor único de perfil (Leve/Médio/Agressivo): um trilho com um polegar
/// que desliza entre três paradas, mais a marca do perfil recomendado embaixo
/// da parada correspondente. Substitui a combinação antiga de "hero com
/// recomendação" + "três cards de nível" por um único sistema visual — a
/// recomendação é uma posição neste controle, não uma afirmação separada.
/// </summary>
public partial class SpectrumSelector : UserControl
{
    public SpectrumSelector()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateThumbPosition(animate: false);
            UpdateRecommendedMark();
            UpdateLabelEmphasis();
        };
    }

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(SpectrumSelector),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIndexChanged));

    public static readonly DependencyProperty RecommendedIndexProperty = DependencyProperty.Register(
        nameof(RecommendedIndex),
        typeof(int),
        typeof(SpectrumSelector),
        new PropertyMetadata(1, (sender, _) => ((SpectrumSelector)sender).UpdateRecommendedMark()));

    public static readonly DependencyProperty Option0LabelProperty = DependencyProperty.Register(
        nameof(Option0Label), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty Option1LabelProperty = DependencyProperty.Register(
        nameof(Option1Label), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty Option2LabelProperty = DependencyProperty.Register(
        nameof(Option2Label), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty RecommendedLabelProperty = DependencyProperty.Register(
        nameof(RecommendedLabel), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public int RecommendedIndex
    {
        get => (int)GetValue(RecommendedIndexProperty);
        set => SetValue(RecommendedIndexProperty, value);
    }

    public string Option0Label
    {
        get => (string)GetValue(Option0LabelProperty);
        set => SetValue(Option0LabelProperty, value);
    }

    public string Option1Label
    {
        get => (string)GetValue(Option1LabelProperty);
        set => SetValue(Option1LabelProperty, value);
    }

    public string Option2Label
    {
        get => (string)GetValue(Option2LabelProperty);
        set => SetValue(Option2LabelProperty, value);
    }

    public string RecommendedLabel
    {
        get => (string)GetValue(RecommendedLabelProperty);
        set => SetValue(RecommendedLabelProperty, value);
    }

    /// <summary>Disparado sempre que <see cref="SelectedIndex"/> muda, seja por clique do
    /// usuário ou por sincronização programática do consumidor.</summary>
    public event EventHandler? SelectionChanged;

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SpectrumSelector)d;
        control.UpdateThumbPosition(animate: true);
        control.UpdateLabelEmphasis();
        control.SelectionChanged?.Invoke(control, EventArgs.Empty);
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SpectrumSelector)d;
        control.Option0Text.Text = control.Option0Label;
        control.Option1Text.Text = control.Option1Label;
        control.Option2Text.Text = control.Option2Label;
        control.RecommendedText0.Text = control.RecommendedLabel;
        control.RecommendedText1.Text = control.RecommendedLabel;
        control.RecommendedText2.Text = control.RecommendedLabel;
        control.UpdateLabelEmphasis();
    }

    private void OnOptionClick(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, Option0Button))
        {
            SelectedIndex = 0;
        }
        else if (ReferenceEquals(sender, Option1Button))
        {
            SelectedIndex = 1;
        }
        else if (ReferenceEquals(sender, Option2Button))
        {
            SelectedIndex = 2;
        }
    }

    private void OnTrackHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThumbPosition(animate: false);
    }

    private void UpdateThumbPosition(bool animate)
    {
        if (TrackHost.ActualWidth <= 0)
        {
            return;
        }

        var segment = TrackHost.ActualWidth / 3;
        var target = (segment * SelectedIndex) + (segment / 2);

        if (animate && MotionPolicy.AnimationsEnabled)
        {
            var animation = new DoubleAnimation(target, (Duration)FindResource("MotionControl"))
            {
                EasingFunction = (System.Windows.Media.Animation.IEasingFunction)FindResource("EaseControl")
            };
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, animation);
            ThumbFocusRingTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }
        else
        {
            ThumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ThumbTransform.X = target;
            ThumbFocusRingTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ThumbFocusRingTransform.X = target;
        }
    }

    /// <summary>Clicking anywhere on the track jumps to its nearest stop, not just the labels above it.</summary>
    private void OnTrackClick(object sender, MouseButtonEventArgs e)
    {
        TrackHost.Focus();
        if (TrackHost.ActualWidth <= 0)
        {
            return;
        }

        var x = e.GetPosition(TrackHost).X;
        var segment = TrackHost.ActualWidth / 3;
        SelectedIndex = Math.Clamp((int)(x / segment), 0, 2);
    }

    private void OnTrackKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            SelectedIndex = Math.Max(0, SelectedIndex - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            SelectedIndex = Math.Min(2, SelectedIndex + 1);
            e.Handled = true;
        }
    }

    private void OnTrackMouseEnter(object sender, MouseEventArgs e) =>
        Thumb.Background = (Brush)FindResource("AccentBrightBrush");

    private void OnTrackMouseLeave(object sender, MouseEventArgs e) =>
        Thumb.Background = (Brush)FindResource("AccentBrush");

    private void OnTrackGotFocus(object sender, KeyboardFocusChangedEventArgs e) => ThumbFocusRing.Opacity = 1;

    private void OnTrackLostFocus(object sender, KeyboardFocusChangedEventArgs e) => ThumbFocusRing.Opacity = 0;

    private void UpdateRecommendedMark()
    {
        Recommended0.Visibility = RecommendedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        Recommended1.Visibility = RecommendedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        Recommended2.Visibility = RecommendedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLabelEmphasis()
    {
        SetEmphasis(Option0Text, SelectedIndex == 0);
        SetEmphasis(Option1Text, SelectedIndex == 1);
        SetEmphasis(Option2Text, SelectedIndex == 2);
    }

    private static void SetEmphasis(TextBlock text, bool selected)
    {
        text.Style = (Style)text.FindResource(selected ? "BodyStrongText" : "SecondaryText");
    }
}
