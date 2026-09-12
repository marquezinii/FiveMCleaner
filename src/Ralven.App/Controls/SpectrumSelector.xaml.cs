using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Ralven.App.Services;
using UserControl = System.Windows.Controls.UserControl;
using TextBlock = System.Windows.Controls.TextBlock;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Ralven.App.Controls;

/// <summary>
/// Seletor único de perfil: um segmentado de até quatro
/// paradas com um indicador que desliza para a selecionada, mais a marca do
/// perfil recomendado embaixo da parada correspondente. Substitui a
/// combinação antiga de "hero com recomendação" + "três cards de nível" por
/// um único sistema visual — a recomendação é uma posição neste controle,
/// não uma afirmação separada.
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
        new PropertyMetadata(-1, (sender, _) => ((SpectrumSelector)sender).UpdateRecommendedMark()));

    public static readonly DependencyProperty Option0LabelProperty = DependencyProperty.Register(
        nameof(Option0Label), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty Option1LabelProperty = DependencyProperty.Register(
        nameof(Option1Label), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty Option2LabelProperty = DependencyProperty.Register(
        nameof(Option2Label), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty Option3LabelProperty = DependencyProperty.Register(
        nameof(Option3Label), typeof(string), typeof(SpectrumSelector), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty ShowOption3Property = DependencyProperty.Register(
        nameof(ShowOption3), typeof(bool), typeof(SpectrumSelector), new PropertyMetadata(true, OnShowOption3Changed));

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

    public string Option3Label
    {
        get => (string)GetValue(Option3LabelProperty);
        set => SetValue(Option3LabelProperty, value);
    }

    public bool ShowOption3
    {
        get => (bool)GetValue(ShowOption3Property);
        set => SetValue(ShowOption3Property, value);
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
        control.Option3Text.Text = control.Option3Label;
        control.RecommendedText0.Text = control.RecommendedLabel;
        control.RecommendedText1.Text = control.RecommendedLabel;
        control.RecommendedText2.Text = control.RecommendedLabel;
        control.UpdateLabelEmphasis();
    }

    private static void OnShowOption3Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SpectrumSelector)d;
        var visibility = control.ShowOption3 ? Visibility.Visible : Visibility.Collapsed;
        control.Option3Column.Width = control.ShowOption3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        control.Option3MarkColumn.Width = control.Option3Column.Width;
        control.Option3Button.Visibility = visibility;
        control.UpdateThumbPosition(animate: false);
    }

    private void OnOptionChecked(object sender, RoutedEventArgs e)
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
        else if (ReferenceEquals(sender, Option3Button))
        {
            SelectedIndex = 3;
        }
    }

    private void OnTrackHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThumbPosition(animate: false);
    }

    private void UpdateThumbPosition(bool animate)
    {
        var segmentCount = ShowOption3 ? 4 : 3;
        SelectionIndicator.Visibility = SelectedIndex >= 0 && SelectedIndex < segmentCount ? Visibility.Visible : Visibility.Collapsed;
        if (SelectedIndex < 0 || SelectedIndex >= segmentCount) return;
        if (TrackHost.ActualWidth <= 0)
        {
            return;
        }

        var segment = TrackHost.ActualWidth / segmentCount;
        var indicatorWidth = Math.Max(0, segment - 6);
        var target = (segment * SelectedIndex) + ((segment - indicatorWidth) / 2);

        SelectionIndicator.Width = indicatorWidth;

        if (animate && MotionPolicy.AnimationsEnabled)
        {
            var animation = new DoubleAnimation(target, (Duration)FindResource("MotionControl"))
            {
                EasingFunction = (System.Windows.Media.Animation.IEasingFunction)FindResource("EaseControl")
            };
            IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }
        else
        {
            IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
            IndicatorTransform.X = target;
        }
    }

    /// <summary>Clicking anywhere on the track jumps to its nearest stop, not just the labels above it.</summary>
    private void OnTrackClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackHost.ActualWidth <= 0)
        {
            return;
        }

        var x = e.GetPosition(TrackHost).X;
        var segmentCount = ShowOption3 ? 4 : 3;
        var segment = TrackHost.ActualWidth / segmentCount;
        SelectedIndex = Math.Clamp((int)(x / segment), 0, segmentCount - 1);
        (SelectedIndex switch
        {
            0 => Option0Button,
            1 => Option1Button,
            2 => Option2Button,
            _ => Option3Button
        }).Focus();
    }

    private void UpdateRecommendedMark()
    {
        Recommended0.Visibility = RecommendedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        Recommended1.Visibility = RecommendedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        Recommended2.Visibility = RecommendedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLabelEmphasis()
    {
        Option0Button.IsChecked = SelectedIndex == 0;
        Option1Button.IsChecked = SelectedIndex == 1;
        Option2Button.IsChecked = SelectedIndex == 2;
        Option3Button.IsChecked = SelectedIndex == 3;
        SetEmphasis(Option0Text, SelectedIndex == 0);
        SetEmphasis(Option1Text, SelectedIndex == 1);
        SetEmphasis(Option2Text, SelectedIndex == 2);
        if (SelectedIndex == 3)
        {
            SelectionIndicator.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ProAccentBrush");
        }
        else
        {
            SelectionIndicator.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }
    }

    private static void SetEmphasis(TextBlock text, bool selected) =>
        text.SetResourceReference(TextBlock.ForegroundProperty, selected ? "TextPrimaryBrush" : "TextSecondaryBrush");
}
