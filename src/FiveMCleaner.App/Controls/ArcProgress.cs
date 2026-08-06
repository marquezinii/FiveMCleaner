using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

// Alias explícito: o projeto também referencia WinForms.
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FiveMCleaner.App.Controls;

/// <summary>
/// Anel de progresso desenhado diretamente, com transição suave entre valores.
/// Usado tanto no medidor de prontidão quanto nos indicadores ao vivo de CPU,
/// GPU, memória e disco, para que a leitura mude por movimento e não por salto.
/// </summary>
public sealed class ArcProgress : FrameworkElement
{
    private static readonly TimeSpan Transition = TimeSpan.FromMilliseconds(760);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(ArcProgress),
        new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(ArcProgress),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Ângulo inicial em graus, medido a partir das 12 horas, no sentido horário.</summary>
    public static readonly DependencyProperty StartAngleProperty = DependencyProperty.Register(
        nameof(StartAngle),
        typeof(double),
        typeof(ArcProgress),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
        nameof(SweepAngle),
        typeof(double),
        typeof(ArcProgress),
        new FrameworkPropertyMetadata(360d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(ArcProgress),
        new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(ArcProgress),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush),
        typeof(Brush),
        typeof(ArcProgress),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty RenderedValueProperty = DependencyProperty.Register(
        nameof(RenderedValue),
        typeof(double),
        typeof(ArcProgress),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StartAngle
    {
        get => (double)GetValue(StartAngleProperty);
        set => SetValue(StartAngleProperty, value);
    }

    public double SweepAngle
    {
        get => (double)GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ProgressBrush
    {
        get => (Brush?)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    private double RenderedValue => (double)GetValue(RenderedValueProperty);

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var arc = (ArcProgress)sender;
        var animation = new DoubleAnimation((double)args.NewValue, Transition)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        arc.BeginAnimation(RenderedValueProperty, animation);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var thickness = StrokeThickness;
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2;
        if (radius <= 0 || thickness <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var sweep = Math.Clamp(SweepAngle, 0, 360);

        if (TrackBrush is { } track)
        {
            Draw(drawingContext, new Pen(track, thickness), center, radius, StartAngle, sweep);
        }

        var maximum = Maximum <= 0 ? 100 : Maximum;
        var fraction = Math.Clamp(RenderedValue / maximum, 0, 1);
        if (ProgressBrush is { } progress && fraction > 0.0005)
        {
            var pen = new Pen(progress, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            Draw(drawingContext, pen, center, radius, StartAngle, sweep * fraction);
        }
    }

    private static void Draw(
        DrawingContext drawingContext,
        Pen pen,
        Point center,
        double radius,
        double startAngle,
        double sweep)
    {
        if (sweep <= 0)
        {
            return;
        }

        if (sweep >= 359.9)
        {
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweep);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweep > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = (angleDegrees - 90) * Math.PI / 180;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
