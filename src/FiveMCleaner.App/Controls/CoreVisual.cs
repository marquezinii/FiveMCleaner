using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

// Alias explícito: o projeto também referencia WinForms.
using Color = System.Windows.Media.Color;

namespace FiveMCleaner.App.Controls;

/// <summary>O que o núcleo 3D está representando no momento.</summary>
public enum CoreVisualMode
{
    /// <summary>Visão geral: só o sólido facetado, girando devagar em repouso.</summary>
    Readiness,

    /// <summary>Otimizador: o mesmo sólido, menor, com três anéis orbitais que
    /// aceleram e brilham conforme <see cref="CoreVisual.Intensity"/>.</summary>
    Engine
}

/// <summary>
/// Elemento visual assinatura do FiveMCleaner: um icosaedro facetado, grafite
/// difuso com reflexo/emissivo laranja de marca — nunca preenchimento sólido.
/// É o mesmo objeto em toda a interface, só o <see cref="Mode"/> muda: na Visão
/// geral ele é o instrumento de prontidão (repouso); no Otimizador ele ganha
/// três toros orbitais e vira o motor que reage à intensidade real do
/// progresso. Substitui os antigos HoloCore3D e OptimizerCore3D.
///
/// Materiais 3D não são <c>DynamicResource</c>-áveis, então o tema chega via
/// <see cref="CoreVisualPalette"/> em vez de recursos XAML.
/// </summary>
public sealed class CoreVisual : Viewport3D
{
    private const double CoreSpinSecondsReadiness = 38;
    private const double CoreSpinSecondsEngine = 26;
    private const double RingBaseSeconds = 34;
    private const double RestingIntensity = 0.20;

    private readonly AxisAngleRotation3D coreSpin = new(new Vector3D(0.22, 1, 0.12), 0);
    private readonly AxisAngleRotation3D outerSpin = new(new Vector3D(0, 0, 1), 0);
    private readonly AxisAngleRotation3D middleSpin = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D innerSpin = new(new Vector3D(1, 0, 0), 0);

    private readonly DiffuseMaterial coreDiffuse = new();
    private readonly SpecularMaterial coreSpecular = new() { SpecularPower = 24 };
    private readonly EmissiveMaterial coreGlow = new();
    private readonly DiffuseMaterial ringDiffuse = new();
    private readonly SpecularMaterial ringSpecular = new() { SpecularPower = 34 };
    private readonly EmissiveMaterial ringGlow = new();
    private readonly AmbientLight ambientLight = new();
    private readonly DirectionalLight keyLight = new();
    private readonly DirectionalLight fillLight = new();

    private readonly ModelVisual3D ringsVisual = new();
    private bool ringsBuilt;

    public CoreVisual()
    {
        ClipToBounds = true;
        Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0, 5.2),
            LookDirection = new Vector3D(0, 0, -1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 42
        };

        var scene = new Model3DGroup();
        scene.Children.Add(ambientLight);
        scene.Children.Add(keyLight);
        scene.Children.Add(fillLight);
        Children.Add(new ModelVisual3D { Content = scene });
        Children.Add(ringsVisual);

        var coreMaterial = new MaterialGroup();
        coreMaterial.Children.Add(coreDiffuse);
        coreMaterial.Children.Add(coreSpecular);
        coreMaterial.Children.Add(coreGlow);
        var coreModel = new GeometryModel3D(FacetedIcosahedronMesh, coreMaterial)
        {
            Transform = new RotateTransform3D(coreSpin, new Point3D(0, 0, 0))
        };
        scene.Children.Add(coreModel);
        this.coreModel = coreModel;

        ApplyPalette();
        UpdateMode();
        CoreVisualPalette.Changed += (_, _) => Dispatcher.Invoke(ApplyPalette);

        Loaded += (_, _) => UpdateAnimations();
        Unloaded += (_, _) => StopAnimations();
        IsVisibleChanged += (_, _) => UpdateAnimations();
    }

    private readonly GeometryModel3D coreModel;

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode),
        typeof(CoreVisualMode),
        typeof(CoreVisual),
        new PropertyMetadata(CoreVisualMode.Readiness, (sender, _) => ((CoreVisual)sender).UpdateMode()));

    /// <summary>Interrompe toda a cena quando a página não está em primeiro plano.</summary>
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.Register(
        nameof(IsLive),
        typeof(bool),
        typeof(CoreVisual),
        new PropertyMetadata(true, (sender, _) => ((CoreVisual)sender).UpdateAnimations()));

    /// <summary>
    /// 0 a 1. Só tem efeito em <see cref="CoreVisualMode.Engine"/>: acelera os
    /// anéis e intensifica o emissivo. Nunca é um número inventado — vem do
    /// nível do perfil selecionado em repouso ou do progresso real em execução.
    /// </summary>
    public static readonly DependencyProperty IntensityProperty = DependencyProperty.Register(
        nameof(Intensity),
        typeof(double),
        typeof(CoreVisual),
        new PropertyMetadata(RestingIntensity, (sender, _) => ((CoreVisual)sender).UpdateAnimations()));

    public CoreVisualMode Mode
    {
        get => (CoreVisualMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public bool IsLive
    {
        get => (bool)GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    public double Intensity
    {
        get => (double)GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    private void UpdateMode()
    {
        var isEngine = Mode == CoreVisualMode.Engine;
        var coreScale = isEngine ? 0.46 : 0.82;
        coreModel.Transform = Combine(
            new ScaleTransform3D(coreScale, coreScale, coreScale),
            new RotateTransform3D(coreSpin, new Point3D(0, 0, 0)));

        if (isEngine)
        {
            EnsureRingsBuilt();
            ringsVisual.Content = ringGroup;
        }
        else
        {
            ringsVisual.Content = null;
        }

        UpdateAnimations();
    }

    private static Transform3DGroup Combine(Transform3D a, Transform3D b)
    {
        var group = new Transform3DGroup();
        group.Children.Add(a);
        group.Children.Add(b);
        return group;
    }

    private Model3DGroup? ringGroup;

    private void EnsureRingsBuilt()
    {
        if (ringsBuilt)
        {
            return;
        }

        ringsBuilt = true;
        ringGroup = new Model3DGroup();
        ringGroup.Children.Add(BuildRing(1.42, 0.055, outerSpin, new Vector3D(0, 0, 1), 0));
        ringGroup.Children.Add(BuildRing(1.14, 0.045, middleSpin, new Vector3D(1, 0.15, 0), 68));
        ringGroup.Children.Add(BuildRing(0.88, 0.038, innerSpin, new Vector3D(0.2, 1, 0), 74));
    }

    private GeometryModel3D BuildRing(double radius, double tube, AxisAngleRotation3D spin, Vector3D tiltAxis, double tiltDegrees)
    {
        var material = new MaterialGroup();
        material.Children.Add(ringDiffuse);
        material.Children.Add(ringSpecular);
        material.Children.Add(ringGlow);

        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(spin, new Point3D(0, 0, 0)));
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(tiltAxis, tiltDegrees), new Point3D(0, 0, 0)));

        return new GeometryModel3D(BuildTorus(radius, tube), material) { Transform = transform };
    }

    private void ApplyPalette()
    {
        var light = CoreVisualPalette.IsLightTheme;

        ambientLight.Color = light ? Color.FromRgb(0x6E, 0x6B, 0x66) : Color.FromRgb(0x1C, 0x1D, 0x22);
        keyLight.Color = light ? Colors.White : Color.FromRgb(0xB4, 0xBA, 0xC6);
        keyLight.Direction = new Vector3D(-0.5, -0.7, -0.8);
        fillLight.Color = light ? Color.FromRgb(0x8A, 0x50, 0x24) : Color.FromRgb(0x2A, 0x30, 0x38);
        fillLight.Direction = new Vector3D(0.8, 0.35, 0.5);

        coreDiffuse.Brush = new SolidColorBrush(light ? Color.FromRgb(0x3A, 0x3D, 0x45) : Color.FromRgb(0x1D, 0x20, 0x27));
        coreSpecular.Brush = new SolidColorBrush(Color.FromArgb(light ? (byte)0x70 : (byte)0xC0, 0xFF, 0x9A, 0x44));
        ringDiffuse.Brush = new SolidColorBrush(light ? Color.FromRgb(0x4A, 0x4D, 0x55) : Color.FromRgb(0x2A, 0x2D, 0x35));
        ringSpecular.Brush = new SolidColorBrush(Color.FromArgb(light ? (byte)0x60 : (byte)0xA0, 0xFF, 0xAA, 0x62));

        UpdateGlow();
    }

    private void StopAnimations()
    {
        coreSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        outerSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        middleSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        innerSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
    }

    private void UpdateAnimations()
    {
        if (!IsLive || !IsLoaded || !IsVisible)
        {
            StopAnimations();
            return;
        }

        var isEngine = Mode == CoreVisualMode.Engine;
        var intensity = isEngine ? Math.Clamp(Intensity, 0, 1) : RestingIntensity;

        if (isEngine)
        {
            var factor = 1 + (intensity * 6.5);
            Spin(coreSpin, CoreSpinSecondsEngine / factor, clockwise: true);
            Spin(outerSpin, RingBaseSeconds / factor, clockwise: false);
            Spin(middleSpin, RingBaseSeconds * 0.74 / factor, clockwise: true);
            Spin(innerSpin, RingBaseSeconds * 0.53 / factor, clockwise: false);
        }
        else
        {
            Spin(coreSpin, CoreSpinSecondsReadiness, clockwise: true);
        }

        UpdateGlow();
    }

    private void UpdateGlow()
    {
        var isEngine = Mode == CoreVisualMode.Engine;
        var intensity = isEngine ? Math.Clamp(Intensity, 0, 1) : RestingIntensity;
        var light = CoreVisualPalette.IsLightTheme;

        // Dark: 0x10–0x3E de alpha. Light: 0x00–0x14 — o objeto é mais escuro
        // que o papel, então o emissivo precisa de bem menos energia para não
        // estourar o contraste.
        var coreAlpha = light
            ? (byte)Math.Round(0x00 + (intensity * 0x14))
            : (byte)Math.Round(0x10 + (intensity * 0x2E));
        var ringAlpha = light
            ? (byte)Math.Round(0x00 + (intensity * 0x18))
            : (byte)Math.Round(0x16 + (intensity * 0x3C));

        coreGlow.Brush = new SolidColorBrush(Color.FromArgb(coreAlpha, 0xFF, 0x7A, 0x18));
        ringGlow.Brush = new SolidColorBrush(Color.FromArgb(ringAlpha, 0xFF, 0x8A, 0x2A));
    }

    private static void Spin(AxisAngleRotation3D rotation, double seconds, bool clockwise)
    {
        var from = clockwise ? 0 : 360;
        var to = clockwise ? 360 : 0;
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(Math.Max(seconds, 0.6)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, animation);
    }

    private static readonly MeshGeometry3D FacetedIcosahedronMesh = BuildFacetedIcosahedron();

    /// <summary>
    /// Icosaedro unitário com vértices duplicados por face, para que o
    /// sombreamento fique facetado em vez de suavizado pela média de normais
    /// do WPF. A escala final é sempre aplicada via <see cref="Transform"/>
    /// (0.82 em repouso, 0.46 como motor) para que o mesmo mesh sirva aos
    /// dois modos.
    /// </summary>
    private static MeshGeometry3D BuildFacetedIcosahedron()
    {
        const double t = 1.6180339887498949;
        Point3D[] vertices =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
        ];

        int[] faces =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        ];

        var positions = new Point3DCollection(faces.Length);
        var indices = new Int32Collection(faces.Length);

        for (var index = 0; index < faces.Length; index++)
        {
            var vertex = vertices[faces[index]];
            positions.Add(vertex);
            indices.Add(index);
        }

        var mesh = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        mesh.Freeze();
        return mesh;
    }

    /// <summary>
    /// Toro fechado com normais explícitas, para que o anel receba luz suave em
    /// vez do facetamento do núcleo — a diferença de acabamento entre as duas
    /// peças é o que dá leitura de profundidade à cena.
    /// </summary>
    private static MeshGeometry3D BuildTorus(double radius, double tube)
    {
        const int major = 56;
        const int minor = 12;

        var positions = new Point3DCollection((major + 1) * (minor + 1));
        var normals = new Vector3DCollection((major + 1) * (minor + 1));
        var indices = new Int32Collection(major * minor * 6);

        for (var i = 0; i <= major; i++)
        {
            var u = 2 * Math.PI * i / major;
            var cosU = Math.Cos(u);
            var sinU = Math.Sin(u);

            for (var j = 0; j <= minor; j++)
            {
                var v = 2 * Math.PI * j / minor;
                var cosV = Math.Cos(v);
                var sinV = Math.Sin(v);

                positions.Add(new Point3D(
                    (radius + (tube * cosV)) * cosU,
                    (radius + (tube * cosV)) * sinU,
                    tube * sinV));
                normals.Add(new Vector3D(cosV * cosU, cosV * sinU, sinV));
            }
        }

        for (var i = 0; i < major; i++)
        {
            for (var j = 0; j < minor; j++)
            {
                var current = (i * (minor + 1)) + j;
                var next = current + minor + 1;

                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);

                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        var mesh = new MeshGeometry3D
        {
            Positions = positions,
            Normals = normals,
            TriangleIndices = indices
        };
        mesh.Freeze();
        return mesh;
    }
}
