using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

// Alias explícito: o projeto também referencia WinForms.
using Color = System.Windows.Media.Color;

namespace FiveMCleaner.App.Controls;

/// <summary>
/// Núcleo do Otimizador: um giroscópio de três anéis em volta de um sólido
/// facetado, desenhado em 3D real (<see cref="Viewport3D"/>).
///
/// A cena é a única peça animada permanente da página, então ela é também o
/// indicador de estado: <see cref="Intensity"/> vai de 0 (parado, à espera) a 1
/// (executando) e controla a velocidade dos anéis e o brilho emissivo laranja
/// do núcleo. Nada aqui inventa medida — a intensidade é sempre alimentada por
/// um estado real da tela (perfil selecionado em repouso, progresso real
/// durante a execução).
/// </summary>
public sealed class OptimizerCore3D : Viewport3D
{
    private const double CoreSpinSeconds = 26;
    private const double RingBaseSeconds = 34;

    private readonly AxisAngleRotation3D coreSpin = new(new Vector3D(0.25, 1, 0.15), 0);
    private readonly AxisAngleRotation3D outerSpin = new(new Vector3D(0, 0, 1), 0);
    private readonly AxisAngleRotation3D middleSpin = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D innerSpin = new(new Vector3D(1, 0, 0), 0);
    private readonly EmissiveMaterial coreGlow = new(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0x7A, 0x18)));
    private readonly EmissiveMaterial ringGlow = new(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0x7A, 0x18)));

    public OptimizerCore3D()
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
        scene.Children.Add(new AmbientLight(Color.FromRgb(0x1C, 0x1D, 0x22)));
        scene.Children.Add(new DirectionalLight(
            Color.FromRgb(0xB4, 0xBA, 0xC6),
            new Vector3D(-0.5, -0.7, -0.8)));
        scene.Children.Add(new DirectionalLight(
            Color.FromRgb(0x6A, 0x40, 0x16),
            new Vector3D(0.8, 0.35, 0.5)));

        scene.Children.Add(BuildCore());
        scene.Children.Add(BuildRing(1.42, 0.055, outerSpin, new Vector3D(0, 0, 1), 0));
        scene.Children.Add(BuildRing(1.14, 0.045, middleSpin, new Vector3D(1, 0.15, 0), 68));
        scene.Children.Add(BuildRing(0.88, 0.038, innerSpin, new Vector3D(0.2, 1, 0), 74));

        Children.Add(new ModelVisual3D { Content = scene });

        Loaded += (_, _) => UpdateAnimations();
        Unloaded += (_, _) => StopAnimations();
        IsVisibleChanged += (_, _) => UpdateAnimations();
    }

    /// <summary>Interrompe a cena quando a página não está em primeiro plano.</summary>
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.Register(
        nameof(IsLive),
        typeof(bool),
        typeof(OptimizerCore3D),
        new PropertyMetadata(true, (sender, _) => ((OptimizerCore3D)sender).UpdateAnimations()));

    /// <summary>
    /// 0 a 1. Acelera os anéis e intensifica o brilho laranja do núcleo.
    /// </summary>
    public static readonly DependencyProperty IntensityProperty = DependencyProperty.Register(
        nameof(Intensity),
        typeof(double),
        typeof(OptimizerCore3D),
        new PropertyMetadata(0.34, (sender, _) => ((OptimizerCore3D)sender).UpdateAnimations()));

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

        // A intensidade encurta a volta completa: em repouso os anéis levam
        // dezenas de segundos; no pico, poucos segundos. A razão é a mesma
        // para todos, então eles nunca "batem" na mesma fase.
        var intensity = Math.Clamp(Intensity, 0, 1);
        var factor = 1 + (intensity * 6.5);

        Spin(coreSpin, CoreSpinSeconds / factor, clockwise: true);
        Spin(outerSpin, RingBaseSeconds / factor, clockwise: false);
        Spin(middleSpin, (RingBaseSeconds * 0.74) / factor, clockwise: true);
        Spin(innerSpin, (RingBaseSeconds * 0.53) / factor, clockwise: false);

        // O emissivo é deliberadamente contido: o núcleo é grafite com
        // reflexo laranja, não um objeto laranja. Saturar demais faria a cena
        // competir com o anel de progresso, que é o dado da tela.
        coreGlow.Brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(0x10 + (intensity * 0x2E)),
            0xFF,
            0x7A,
            0x18));
        ringGlow.Brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(0x16 + (intensity * 0x3C)),
            0xFF,
            0x8A,
            0x2A));
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

    private GeometryModel3D BuildCore()
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x1D, 0x20, 0x27))));
        material.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x9A, 0x44)),
            26));
        material.Children.Add(coreGlow);

        return new GeometryModel3D(BuildFacetedIcosahedron(0.46), material)
        {
            Transform = new RotateTransform3D(coreSpin, new Point3D(0, 0, 0))
        };
    }

    private GeometryModel3D BuildRing(double radius, double tube, AxisAngleRotation3D spin, Vector3D tiltAxis, double tiltDegrees)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x35))));
        material.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xAA, 0x62)),
            34));
        material.Children.Add(ringGlow);

        // A inclinação fixa posiciona o plano do anel; o giro roda em torno do
        // eixo próprio depois disso, então os três anéis descrevem órbitas
        // visivelmente diferentes em vez de três círculos concêntricos.
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(spin, new Point3D(0, 0, 0)));
        transform.Children.Add(new RotateTransform3D(
            new AxisAngleRotation3D(tiltAxis, tiltDegrees),
            new Point3D(0, 0, 0)));

        return new GeometryModel3D(BuildTorus(radius, tube), material) { Transform = transform };
    }

    /// <summary>
    /// Icosaedro com vértices duplicados por face, para que o sombreamento
    /// fique facetado em vez de suavizado pela média de normais do WPF.
    /// </summary>
    private static MeshGeometry3D BuildFacetedIcosahedron(double scale)
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
            positions.Add(new Point3D(vertex.X * scale, vertex.Y * scale, vertex.Z * scale));
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
