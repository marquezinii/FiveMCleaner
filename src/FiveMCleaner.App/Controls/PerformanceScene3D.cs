using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

// O projeto também referencia WinForms, então os tipos gráficos precisam de
// alias explícito para não colidirem com System.Drawing.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace FiveMCleaner.App.Controls;

/// <summary>
/// Cena 3D ao vivo do desempenho local. Duas fitas extrudadas (CPU e GPU)
/// crescem sobre um piso em grade, com iluminação direcional e uma oscilação
/// lenta de câmera. Os valores exibidos perseguem as amostras reais por
/// interpolação, então o movimento continua contínuo mesmo com a coleta real
/// acontecendo apenas a cada dois segundos.
/// </summary>
/// <remarks>
/// O laço de renderização só existe enquanto <see cref="IsLive"/> é verdadeiro
/// e o controle está carregado; a página da Visão geral já pausa a coleta ao
/// navegar para outra seção, e a cena acompanha essa mesma decisão.
/// </remarks>
public sealed class PerformanceScene3D : Viewport3D
{
    private const int DefaultCapacity = 30;
    private const double SceneWidth = 12.4;
    private const double RibbonMaxHeight = 2.35;
    private const double RibbonDepth = 0.42;
    private const double FloorDepth = 2.2;
    private const double EaseFactor = 0.16;
    private const double SwayDegrees = 7.5;

    // 20 quadros por segundo bastam: a oscilação da câmera é lenta e a
    // interpolação das amostras dura cerca de um segundo. Subir a taxa aqui
    // custa CPU sem ganho visível.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly Model3DGroup sceneModel = new();
    private readonly AxisAngleRotation3D sway = new(new Vector3D(0, 1, 0), 0);
    private readonly Ribbon cpuRibbon;
    private readonly Ribbon gpuRibbon;

    private bool rendering;
    private TimeSpan lastFrame = TimeSpan.MinValue;
    private double swayPhase;

    public PerformanceScene3D()
    {
        ClipToBounds = true;
        cpuRibbon = new Ribbon(DefaultCapacity, RibbonDepth / 2 + 0.42);
        gpuRibbon = new Ribbon(DefaultCapacity, -(RibbonDepth / 2 + 0.42));

        // O quadro da cena é largo e baixo: a câmera precisa recuar o bastante
        // para caber toda a janela de amostras sem cortar os picos.
        Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 7.6, 16.2),
            LookDirection = new Vector3D(0, -0.44, -1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 40
        };

        sceneModel.Children.Add(new AmbientLight(Color.FromRgb(0x3A, 0x3C, 0x44)));
        sceneModel.Children.Add(new DirectionalLight(
            Color.FromRgb(0xC8, 0xCC, 0xD6),
            new Vector3D(-0.45, -0.86, -0.55)));
        sceneModel.Children.Add(BuildFloor());
        sceneModel.Children.Add(cpuRibbon.Model);
        sceneModel.Children.Add(gpuRibbon.Model);
        sceneModel.Transform = new RotateTransform3D(sway, new Point3D(0, 0, 0));

        Children.Add(new ModelVisual3D { Content = sceneModel });

        ApplySeriesColor(cpuRibbon, CpuColor);
        ApplySeriesColor(gpuRibbon, GpuColor);

        Loaded += (_, _) => UpdateRenderingState();
        Unloaded += (_, _) => StopRendering();
        IsVisibleChanged += (_, _) => UpdateRenderingState();
    }

    /// <summary>Amostras de CPU em porcentagem, da mais antiga para a mais recente.</summary>
    public static readonly DependencyProperty CpuValuesProperty = DependencyProperty.Register(
        nameof(CpuValues),
        typeof(IReadOnlyList<double>),
        typeof(PerformanceScene3D),
        new PropertyMetadata(null, OnCpuValuesChanged));

    /// <summary>Amostras de GPU em porcentagem, da mais antiga para a mais recente.</summary>
    public static readonly DependencyProperty GpuValuesProperty = DependencyProperty.Register(
        nameof(GpuValues),
        typeof(IReadOnlyList<double>),
        typeof(PerformanceScene3D),
        new PropertyMetadata(null, OnGpuValuesChanged));

    public static readonly DependencyProperty CpuColorProperty = DependencyProperty.Register(
        nameof(CpuColor),
        typeof(Color),
        typeof(PerformanceScene3D),
        new PropertyMetadata(Color.FromRgb(0xFF, 0x7A, 0x18), OnCpuColorChanged));

    public static readonly DependencyProperty GpuColorProperty = DependencyProperty.Register(
        nameof(GpuColor),
        typeof(Color),
        typeof(PerformanceScene3D),
        new PropertyMetadata(Color.FromRgb(0x62, 0xA8, 0xFF), OnGpuColorChanged));

    /// <summary>Mantém a animação viva apenas enquanto a Visão geral está ativa.</summary>
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.Register(
        nameof(IsLive),
        typeof(bool),
        typeof(PerformanceScene3D),
        new PropertyMetadata(true, OnIsLiveChanged));

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

    public Color CpuColor
    {
        get => (Color)GetValue(CpuColorProperty);
        set => SetValue(CpuColorProperty, value);
    }

    public Color GpuColor
    {
        get => (Color)GetValue(GpuColorProperty);
        set => SetValue(GpuColorProperty, value);
    }

    public bool IsLive
    {
        get => (bool)GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    private static void OnCpuValuesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (PerformanceScene3D)sender;
        scene.cpuRibbon.SetTarget(args.NewValue as IReadOnlyList<double>);
        scene.UpdateRenderingState();
    }

    private static void OnGpuValuesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var scene = (PerformanceScene3D)sender;
        scene.gpuRibbon.SetTarget(args.NewValue as IReadOnlyList<double>);
        scene.UpdateRenderingState();
    }

    private static void OnCpuColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ApplySeriesColor(((PerformanceScene3D)sender).cpuRibbon, (Color)args.NewValue);

    private static void OnGpuColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ApplySeriesColor(((PerformanceScene3D)sender).gpuRibbon, (Color)args.NewValue);

    private static void OnIsLiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((PerformanceScene3D)sender).UpdateRenderingState();

    private void UpdateRenderingState()
    {
        if (IsLive && IsLoaded && IsVisible)
        {
            StartRendering();
            return;
        }

        StopRendering();
    }

    private void StartRendering()
    {
        if (rendering)
        {
            return;
        }

        rendering = true;
        lastFrame = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnRenderingFrame;
    }

    private void StopRendering()
    {
        if (!rendering)
        {
            return;
        }

        rendering = false;
        CompositionTarget.Rendering -= OnRenderingFrame;
    }

    private void OnRenderingFrame(object? sender, EventArgs args)
    {
        if (args is not RenderingEventArgs rendering)
        {
            return;
        }

        if (lastFrame != TimeSpan.MinValue && rendering.RenderingTime - lastFrame < FrameInterval)
        {
            return;
        }

        lastFrame = rendering.RenderingTime;

        swayPhase += 0.0085;
        sway.Angle = SwayDegrees * Math.Sin(swayPhase);

        cpuRibbon.Advance();
        gpuRibbon.Advance();
    }

    private static void ApplySeriesColor(Ribbon ribbon, Color color)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        gradient.GradientStops.Add(new GradientStop(Lighten(color, 0.04), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x8E, color.R, color.G, color.B), 0.14));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, color.R, color.G, color.B), 0.55));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x08, color.R, color.G, color.B), 1));
        gradient.Freeze();

        var glow = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x5E, color.R, color.G, color.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x1C, color.R, color.G, color.B), 0.16));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 0.72));
        glow.Freeze();

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(gradient));
        material.Children.Add(new EmissiveMaterial(glow));
        ribbon.Model.Material = material;
        ribbon.Model.BackMaterial = material;
    }

    private static Color Lighten(Color color, double amount) => Color.FromArgb(
        0xFF,
        (byte)Math.Clamp(color.R + (255 - color.R) * amount, 0, 255),
        (byte)Math.Clamp(color.G + (255 - color.G) * amount, 0, 255),
        (byte)Math.Clamp(color.B + (255 - color.B) * amount, 0, 255));

    private static GeometryModel3D BuildFloor()
    {
        var half = SceneWidth / 2 + 0.6;
        var mesh = new MeshGeometry3D
        {
            Positions =
            [
                new Point3D(-half, 0, FloorDepth),
                new Point3D(half, 0, FloorDepth),
                new Point3D(half, 0, -FloorDepth),
                new Point3D(-half, 0, -FloorDepth)
            ],
            TextureCoordinates =
            [
                new Point(0, 1),
                new Point(1, 1),
                new Point(1, 0),
                new Point(0, 0)
            ],
            TriangleIndices = [0, 1, 2, 0, 2, 3]
        };

        var model = new GeometryModel3D(mesh, new DiffuseMaterial(CreateGridBrush()));
        model.BackMaterial = model.Material;
        return model;
    }

    private static Brush CreateGridBrush()
    {
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(1, 0)));
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 1)));

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x0F)),
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x8E, 0x96, 0xA6)), 0.035),
            lines));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 0.0625, 0.125),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Uma fita: malha com topologia fixa cujas posições são reescritas a cada
    /// quadro. Evitar recriar a malha mantém o custo do painel próximo do que
    /// era com o gráfico 2D anterior.
    /// </summary>
    private sealed class Ribbon
    {
        private readonly int capacity;
        private readonly double z;
        private readonly double[] display;
        private readonly double[] target;
        private int activeCount;

        internal Ribbon(int capacity, double z)
        {
            this.capacity = capacity;
            this.z = z;
            display = new double[capacity];
            target = new double[capacity];

            var mesh = new MeshGeometry3D();
            var positions = new Point3DCollection(capacity * 3);
            var coordinates = new PointCollection(capacity * 3);
            var indices = new Int32Collection((capacity - 1) * 12);

            for (var index = 0; index < capacity; index++)
            {
                var x = XAt(index, capacity);
                positions.Add(new Point3D(x, 0, z + RibbonDepth / 2));
                positions.Add(new Point3D(x, 0, z + RibbonDepth / 2));
                positions.Add(new Point3D(x, 0, z - RibbonDepth / 2));

                var u = capacity == 1 ? 0 : (double)index / (capacity - 1);
                coordinates.Add(new Point(u, 1));
                coordinates.Add(new Point(u, 0));
                coordinates.Add(new Point(u, 0));
            }

            for (var index = 0; index < capacity - 1; index++)
            {
                var current = index * 3;
                var next = current + 3;

                // Face frontal.
                indices.Add(current);
                indices.Add(next + 1);
                indices.Add(current + 1);
                indices.Add(current);
                indices.Add(next);
                indices.Add(next + 1);

                // Face superior (a aresta que brilha).
                indices.Add(current + 1);
                indices.Add(next + 1);
                indices.Add(next + 2);
                indices.Add(current + 1);
                indices.Add(next + 2);
                indices.Add(current + 2);
            }

            mesh.Positions = positions;
            mesh.TextureCoordinates = coordinates;
            mesh.TriangleIndices = indices;
            Model = new GeometryModel3D { Geometry = mesh };
        }

        internal GeometryModel3D Model { get; }

        /// <summary>
        /// As amostras disponíveis sempre ocupam a largura inteira da cena. Uma
        /// série ainda incompleta não deixa um trecho zerado à esquerda: os
        /// vértices excedentes colapsam sobre o último ponto real e somem.
        /// </summary>
        internal void SetTarget(IReadOnlyList<double>? values)
        {
            Array.Clear(target);
            activeCount = values is null ? 0 : Math.Min(values.Count, capacity);
            for (var index = 0; index < activeCount; index++)
            {
                target[index] = Math.Clamp(values![values.Count - activeCount + index], 0, 100);
            }
        }

        internal void Advance()
        {
            var mesh = (MeshGeometry3D)Model.Geometry;
            var positions = mesh.Positions;
            var count = activeCount;

            for (var index = 0; index < capacity; index++)
            {
                var delta = target[index] - display[index];
                display[index] = Math.Abs(delta) < 0.01
                    ? target[index]
                    : display[index] + delta * EaseFactor;
            }

            for (var index = 0; index < capacity; index++)
            {
                // Índices sem amostra recebem a posição do último ponto real,
                // o que degenera seus triângulos em área zero.
                var sample = count == 0 ? 0 : Math.Min(index, count - 1);
                var height = display[sample] / 100 * RibbonMaxHeight;
                var x = XAt(sample, count);
                positions[index * 3] = new Point3D(x, 0, z + RibbonDepth / 2);
                positions[index * 3 + 1] = new Point3D(x, height, z + RibbonDepth / 2);
                positions[index * 3 + 2] = new Point3D(x, height, z - RibbonDepth / 2);
            }
        }

        private static double XAt(int index, int count) => count <= 1
            ? 0
            : -SceneWidth / 2 + index * SceneWidth / (count - 1);
    }
}
