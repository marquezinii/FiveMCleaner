using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

// O projeto também referencia WinForms, então os tipos gráficos precisam de
// alias explícito para não colidirem com System.Drawing.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Ralven.App.Controls;

/// <summary>
/// Gráfico 2D de uma ou duas séries ao vivo. Desenha uma janela de tempo fixa
/// (<see cref="Capacity"/> amostras, mais recente à direita), com escala,
/// área preenchida e leitura por mouse ou teclado.
/// </summary>
/// <remarks>
/// Não existe laço de animação: o controle só redesenha quando chega uma
/// amostra nova ou quando o ponteiro se move sobre ele. Esse é o motivo de o
/// painel voltar a ser barato — a janela não precisa compor a 60 quadros por
/// segundo o tempo inteiro, como acontecia com a cena 3D anterior.
/// </remarks>
public sealed class LivePerformanceChart : FrameworkElement
{
    private const double TopPadding = 12;
    private const double LeftPadding = 42;
    private const double RightPadding = 8;
    private const double BottomPadding = 24;
    private const double TooltipPadding = 9;
    private const double TooltipGap = 12;

    private static FontFamily LabelFont =>
        System.Windows.Application.Current?.TryFindResource("AppFontBody") as FontFamily
        ?? new FontFamily("Inter, Segoe UI Variable Text, Segoe UI");

    private int hoverIndex = -1;

    public LivePerformanceChart()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>Amostras de CPU em porcentagem, da mais antiga para a mais recente.</summary>
    public static readonly DependencyProperty CpuValuesProperty = DependencyProperty.Register(
        nameof(CpuValues),
        typeof(IReadOnlyList<double>),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Amostras de GPU em porcentagem, na mesma ordem das de CPU.</summary>
    public static readonly DependencyProperty GpuValuesProperty = DependencyProperty.Register(
        nameof(GpuValues),
        typeof(IReadOnlyList<double>),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Quantidade de amostras que cabem na janela desenhada.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity),
        typeof(int),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Segundos entre amostras, usado apenas para rotular o ponto sob o cursor.</summary>
    public static readonly DependencyProperty SampleIntervalSecondsProperty = DependencyProperty.Register(
        nameof(SampleIntervalSeconds),
        typeof(double),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CpuBrushProperty = DependencyProperty.Register(
        nameof(CpuBrush),
        typeof(Brush),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GpuBrushProperty = DependencyProperty.Register(
        nameof(GpuBrush),
        typeof(Brush),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush),
        typeof(Brush),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush),
        typeof(Brush),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TooltipBackgroundProperty = DependencyProperty.Register(
        nameof(TooltipBackground),
        typeof(Brush),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TooltipBorderBrushProperty = DependencyProperty.Register(
        nameof(TooltipBorderBrush),
        typeof(Brush),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CpuLabelProperty = DependencyProperty.Register(
        nameof(CpuLabel),
        typeof(string),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata("CPU", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GpuLabelProperty = DependencyProperty.Register(
        nameof(GpuLabel),
        typeof(string),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata("GPU", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Word shown for the most recent sample in the hover reading.</summary>
    public static readonly DependencyProperty NowLabelProperty = DependencyProperty.Register(
        nameof(NowLabel),
        typeof(string),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata("now", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowLabelProperty = DependencyProperty.Register(
        nameof(WindowLabel),
        typeof(string),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata("60s", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumValueProperty = DependencyProperty.Register(
        nameof(MaximumValue),
        typeof(double),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat),
        typeof(string),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata("0", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueSuffixProperty = DependencyProperty.Register(
        nameof(ValueSuffix),
        typeof(string),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata("%", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid),
        typeof(bool),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowAreaProperty = DependencyProperty.Register(
        nameof(ShowArea),
        typeof(bool),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowTooltipProperty = DependencyProperty.Register(
        nameof(ShowTooltip),
        typeof(bool),
        typeof(LivePerformanceChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? CpuValues
    {
        get => (IReadOnlyList<double>?)GetValue(CpuValuesProperty);
        set => SetValue(CpuValuesProperty, value);
    }

    public IReadOnlyList<double>? GpuValues
    {
        get => (IReadOnlyList<double>?)GetValue(GpuValuesProperty);
        set => SetValue(GpuValuesProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public double SampleIntervalSeconds
    {
        get => (double)GetValue(SampleIntervalSecondsProperty);
        set => SetValue(SampleIntervalSecondsProperty, value);
    }

    public Brush? CpuBrush
    {
        get => (Brush?)GetValue(CpuBrushProperty);
        set => SetValue(CpuBrushProperty, value);
    }

    public Brush? GpuBrush
    {
        get => (Brush?)GetValue(GpuBrushProperty);
        set => SetValue(GpuBrushProperty, value);
    }

    public Brush? GridBrush
    {
        get => (Brush?)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush? LabelBrush
    {
        get => (Brush?)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush? TooltipBackground
    {
        get => (Brush?)GetValue(TooltipBackgroundProperty);
        set => SetValue(TooltipBackgroundProperty, value);
    }

    public Brush? TooltipBorderBrush
    {
        get => (Brush?)GetValue(TooltipBorderBrushProperty);
        set => SetValue(TooltipBorderBrushProperty, value);
    }

    public string CpuLabel
    {
        get => (string)GetValue(CpuLabelProperty);
        set => SetValue(CpuLabelProperty, value);
    }

    public string GpuLabel
    {
        get => (string)GetValue(GpuLabelProperty);
        set => SetValue(GpuLabelProperty, value);
    }

    public string NowLabel
    {
        get => (string)GetValue(NowLabelProperty);
        set => SetValue(NowLabelProperty, value);
    }

    public string WindowLabel
    {
        get => (string)GetValue(WindowLabelProperty);
        set => SetValue(WindowLabelProperty, value);
    }

    public double MaximumValue
    {
        get => (double)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public string ValueSuffix
    {
        get => (string)GetValue(ValueSuffixProperty);
        set => SetValue(ValueSuffixProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool ShowArea
    {
        get => (bool)GetValue(ShowAreaProperty);
        set => SetValue(ShowAreaProperty, value);
    }

    public bool ShowTooltip
    {
        get => (bool)GetValue(ShowTooltipProperty);
        set => SetValue(ShowTooltipProperty, value);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!ShowTooltip)
        {
            return;
        }

        var plot = PlotBounds(ActualWidth, ActualHeight);
        var index = IndexAt(
            e.GetPosition(this).X,
            plot.Left,
            plot.Right,
            Math.Max(Capacity, 2),
            PointCount);
        if (index == hoverIndex)
        {
            return;
        }

        hoverIndex = index;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverIndex < 0 || IsKeyboardFocusWithin)
        {
            return;
        }

        hoverIndex = -1;
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ShowTooltip && PointCount > 0)
        {
            hoverIndex = 0;
            InvalidateVisual();
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        hoverIndex = -1;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!ShowTooltip || PointCount == 0)
        {
            return;
        }

        var next = e.Key switch
        {
            Key.Left => Math.Min(Math.Max(hoverIndex, 0) + 1, PointCount - 1),
            Key.Right => Math.Max(hoverIndex - 1, 0),
            Key.Home => PointCount - 1,
            Key.End => 0,
            Key.Escape => -1,
            _ => hoverIndex
        };
        if (next == hoverIndex)
        {
            return;
        }

        hoverIndex = next;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 8 || height <= 8)
        {
            return;
        }

        // Um FrameworkElement sem nada desenhado não recebe o ponteiro; esta
        // superfície transparente é o que torna a leitura sob o cursor possível.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var plot = PlotBounds(width, height);
        DrawGrid(drawingContext, plot);

        // A GPU fica atrás: quando as duas séries se encostam, a leitura de CPU
        // — a mais consultada — continua legível.
        DrawSeries(drawingContext, GpuValues, GpuBrush, plot);
        DrawSeries(drawingContext, CpuValues, CpuBrush, plot);

        DrawHover(drawingContext, plot, width, height);
    }

    private void DrawGrid(DrawingContext drawingContext, Rect plot)
    {
        if (!ShowGrid || GridBrush is not { } grid)
        {
            return;
        }

        var pen = new Pen(grid, 1)
        {
            DashStyle = new DashStyle([3, 5], 0)
        };
        pen.Freeze();

        foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
        {
            var y = Math.Round(ValueToY(fraction * Maximum, plot)) + 0.5;
            drawingContext.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        if (LabelBrush is { } labels)
        {
            foreach (var fraction in new[] { 0d, 0.5, 1d })
            {
                var text = CreateText(FormatValue(fraction * Maximum), 9, labels);
                var y = ValueToY(fraction * Maximum, plot) - text.Height / 2;
                drawingContext.DrawText(text, new Point(4, Math.Clamp(y, 0, plot.Bottom - text.Height)));
            }

            var window = CreateText(WindowLabel, 9, labels);
            drawingContext.DrawText(window, new Point(plot.Left, plot.Bottom + 6));
            var now = CreateText(NowLabel, 9, labels);
            drawingContext.DrawText(now, new Point(plot.Right - now.Width, plot.Bottom + 6));
        }
    }

    private void DrawSeries(
        DrawingContext drawingContext,
        IReadOnlyList<double>? values,
        Brush? brush,
        Rect plot)
    {
        if (brush is null || values is null || values.Count == 0)
        {
            return;
        }

        var count = Math.Min(values.Count, Math.Max(Capacity, 2));
        var offset = values.Count - count;
        var step = StepFor(plot.Width, Math.Max(Capacity, 2));
        var first = plot.Right - (count - 1) * step;

        if (count == 1)
        {
            drawingContext.DrawEllipse(
                brush,
                null,
                new Point(first, ValueToY(values[offset], plot)),
                2,
                2);
            return;
        }

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lineContext = line.Open())
        using (var areaContext = area.Open())
        {
            var start = new Point(first, ValueToY(values[offset], plot));
            lineContext.BeginFigure(start, false, false);
            areaContext.BeginFigure(new Point(first, plot.Bottom), true, true);
            areaContext.LineTo(start, false, false);

            for (var index = 1; index < count; index++)
            {
                var point = new Point(first + index * step, ValueToY(values[offset + index], plot));
                lineContext.LineTo(point, true, false);
                areaContext.LineTo(point, false, false);
            }

            areaContext.LineTo(new Point(first + (count - 1) * step, plot.Bottom), false, false);
        }

        line.Freeze();
        area.Freeze();

        if (ShowArea)
        {
            drawingContext.DrawGeometry(CreateAreaBrush(brush), null, area);
        }
        var pen = new Pen(brush, 1.6)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, line);
    }

    private void DrawHover(DrawingContext drawingContext, Rect plot, double width, double height)
    {
        if (!ShowTooltip || hoverIndex < 0)
        {
            return;
        }

        var x = Math.Round(plot.Right - hoverIndex * StepFor(plot.Width, Math.Max(Capacity, 2))) + 0.5;
        if (x < plot.Left || x > plot.Right)
        {
            return;
        }

        var cpu = SampleAt(CpuValues, hoverIndex);
        var gpu = SampleAt(GpuValues, hoverIndex);
        if (cpu is null && gpu is null)
        {
            return;
        }

        if (GridBrush is { } grid)
        {
            var pen = new Pen(grid, 1);
            pen.Freeze();
            drawingContext.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        DrawMarker(drawingContext, x, gpu, GpuBrush, plot);
        DrawMarker(drawingContext, x, cpu, CpuBrush, plot);
        DrawTooltip(drawingContext, x, cpu, gpu, width, height);
    }

    private void DrawMarker(DrawingContext drawingContext, double x, double? value, Brush? brush, Rect plot)
    {
        if (value is not { } sample || brush is null)
        {
            return;
        }

        var center = new Point(x, ValueToY(sample, plot));
        var pen = new Pen(brush, 2);
        pen.Freeze();
        drawingContext.DrawEllipse(TooltipBackground, pen, center, 3.5, 3.5);
    }

    private void DrawTooltip(
        DrawingContext drawingContext,
        double x,
        double? cpu,
        double? gpu,
        double width,
        double height)
    {
        if (TooltipBackground is not { } background || LabelBrush is not { } labels)
        {
            return;
        }

        var seconds = hoverIndex * SampleIntervalSeconds;
        var lines = new List<(FormattedText Text, Brush? Dot)>
        {
            (CreateText(
                seconds <= 0
                    ? NowLabel
                    : "-" + seconds.ToString("0", CultureInfo.CurrentCulture) + "s",
                9,
                labels),
                null)
        };

        if (cpu is { } cpuValue)
        {
            lines.Add((CreateText($"{CpuLabel}  {FormatValue(cpuValue)}", 10.5, labels), CpuBrush));
        }

        if (gpu is { } gpuValue)
        {
            lines.Add((CreateText($"{GpuLabel}  {FormatValue(gpuValue)}", 10.5, labels), GpuBrush));
        }

        const double dotColumn = 12;
        var contentWidth = 0d;
        var contentHeight = 0d;
        foreach (var (text, dot) in lines)
        {
            contentWidth = Math.Max(contentWidth, text.Width + (dot is null ? 0 : dotColumn));
            contentHeight += text.Height + 2;
        }

        var boxWidth = contentWidth + 2 * TooltipPadding;
        var boxHeight = contentHeight + 2 * TooltipPadding;

        // Preferência pela direita do cursor; perto da borda direita, a caixa
        // passa para a esquerda em vez de encostar e cobrir a leitura.
        var left = x + TooltipGap + boxWidth <= width - 4
            ? x + TooltipGap
            : x - TooltipGap - boxWidth;

        var box = new Rect(
            Math.Clamp(left, 4, Math.Max(4, width - boxWidth - 4)),
            Math.Clamp(height / 2 - boxHeight / 2, 4, Math.Max(4, height - boxHeight - 4)),
            boxWidth,
            boxHeight);

        var border = TooltipBorderBrush is null ? null : new Pen(TooltipBorderBrush, 1);
        border?.Freeze();
        drawingContext.DrawRoundedRectangle(background, border, box, 8, 8);

        var y = box.Y + TooltipPadding;
        foreach (var (text, dot) in lines)
        {
            var textX = box.X + TooltipPadding;
            if (dot is not null)
            {
                drawingContext.DrawRectangle(
                    dot,
                    null,
                    new Rect(textX, y + text.Height / 2 - 1.5, 7, 3));
                textX += dotColumn;
            }

            drawingContext.DrawText(text, new Point(textX, y));
            y += text.Height + 2;
        }
    }

    /// <summary>
    /// Amostras efetivamente desenhadas. O passo horizontal continua preso à
    /// capacidade para que dez segundos de dados nunca pareçam um minuto.
    /// </summary>
    private int PointCount => Math.Min(
        Math.Max(CpuValues?.Count ?? 0, GpuValues?.Count ?? 0),
        Math.Max(Capacity, 2));

    internal static double StepFor(double width, int count) => count > 1 ? width / (count - 1) : width;

    /// <summary>
    /// Índice contado a partir da amostra mais recente (0 = agora); -1 quando o
    /// ponteiro está fora da janela desenhada.
    /// </summary>
    internal static int IndexAt(double x, double width, int count)
        => IndexAt(x, 0, width, count, count);

    internal static int IndexAt(
        double x,
        double left,
        double right,
        int capacity,
        int sampleCount)
    {
        var width = right - left;
        if (width <= 8 || capacity < 2 || sampleCount <= 0 || x < left || x > right)
        {
            return -1;
        }

        var index = (int)Math.Round((right - x) / StepFor(width, capacity));
        return index < 0 || index >= sampleCount ? -1 : index;
    }

    internal static double? SampleAt(IReadOnlyList<double>? values, int indexFromNewest)
    {
        if (values is null)
        {
            return null;
        }

        var index = values.Count - 1 - indexFromNewest;
        return index < 0 || index >= values.Count ? null : values[index];
    }

    internal static double ValueToY(double value, double height)
    {
        var usable = Math.Max(height - TopPadding, 1);
        return height - Math.Clamp(value, 0, 100) / 100 * usable;
    }

    internal static double ValueToY(double value, double maximumValue, Rect plot)
    {
        var maximum = Math.Max(maximumValue, double.Epsilon);
        return plot.Bottom - Math.Clamp(value, 0, maximum) / maximum * plot.Height;
    }

    private double ValueToY(double value, Rect plot) => ValueToY(value, Maximum, plot);

    private double Maximum => Math.Max(MaximumValue, double.Epsilon);

    private Rect PlotBounds(double width, double height)
    {
        if (!ShowGrid)
        {
            return new Rect(1, 2, Math.Max(width - 2, 1), Math.Max(height - 4, 1));
        }

        return new Rect(
            LeftPadding,
            TopPadding,
            Math.Max(width - LeftPadding - RightPadding, 1),
            Math.Max(height - TopPadding - BottomPadding, 1));
    }

    private string FormatValue(double value) =>
        value.ToString(ValueFormat, CultureInfo.CurrentCulture) + ValueSuffix;

    private static Brush CreateAreaBrush(Brush source)
    {
        var color = source is SolidColorBrush solid ? solid.Color : Colors.White;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x3C, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1));
        brush.Freeze();
        return brush;
    }

    private FormattedText CreateText(string text, double size, Brush brush) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(LabelFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip)
    {
        TextAlignment = TextAlignment.Left
    };
}
