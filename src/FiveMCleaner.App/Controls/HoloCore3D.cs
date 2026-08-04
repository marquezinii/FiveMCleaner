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
/// Núcleo poliédrico 3D que gira lentamente atrás do medidor de prontidão.
/// As facetas planas capturam a luz direcional em ângulos diferentes durante a
/// rotação, o que dá profundidade ao bloco sem introduzir cor nova: o brilho
/// especular usa o mesmo laranja da marca, reservado a destaque.
/// </summary>
public sealed class HoloCore3D : Viewport3D
{
    private readonly AxisAngleRotation3D spin = new(new Vector3D(0.22, 1, 0.12), 0);

    public HoloCore3D()
    {
        ClipToBounds = true;
        Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0, 5.2),
            LookDirection = new Vector3D(0, 0, -1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 42
        };

        var model = new Model3DGroup();
        model.Children.Add(new AmbientLight(Color.FromRgb(0x1C, 0x1D, 0x22)));
        model.Children.Add(new DirectionalLight(
            Color.FromRgb(0xB4, 0xBA, 0xC6),
            new Vector3D(-0.5, -0.7, -0.8)));
        model.Children.Add(new DirectionalLight(
            Color.FromRgb(0x5A, 0x38, 0x14),
            new Vector3D(0.8, 0.35, 0.5)));

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x1D, 0x20, 0x27))));
        material.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0x9A, 0x44)),
            22));
        material.Children.Add(new EmissiveMaterial(
            new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0x7A, 0x18))));

        var solid = new GeometryModel3D(BuildIcosahedron(), material);
        solid.Transform = new RotateTransform3D(spin, new Point3D(0, 0, 0));
        model.Children.Add(solid);

        Children.Add(new ModelVisual3D { Content = model });

        Loaded += (_, _) => UpdateSpin();
        Unloaded += (_, _) => spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        IsVisibleChanged += (_, _) => UpdateSpin();
    }

    /// <summary>Interrompe a rotação quando a Visão geral não está ativa.</summary>
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.Register(
        nameof(IsLive),
        typeof(bool),
        typeof(HoloCore3D),
        new PropertyMetadata(true, (sender, _) => ((HoloCore3D)sender).UpdateSpin()));

    public bool IsLive
    {
        get => (bool)GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    private void UpdateSpin()
    {
        if (!IsLive || !IsLoaded || !IsVisible)
        {
            spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            return;
        }

        var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(38))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, animation);
    }

    /// <summary>
    /// Icosaedro com vértices duplicados por face, para que o sombreamento
    /// fique facetado em vez de suavizado pela média de normais do WPF.
    /// </summary>
    private static MeshGeometry3D BuildIcosahedron()
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

        var mesh = new MeshGeometry3D();
        var positions = new Point3DCollection(faces.Length);
        var indices = new Int32Collection(faces.Length);
        // O sólido precisa caber inteiro no quadro: com a câmera em z = 5.2 e
        // campo de 42°, a metade visível é ~2.0, e o raio aqui fica em ~1.56.
        const double scale = 0.82;

        for (var index = 0; index < faces.Length; index++)
        {
            var vertex = vertices[faces[index]];
            positions.Add(new Point3D(vertex.X * scale, vertex.Y * scale, vertex.Z * scale));
            indices.Add(index);
        }

        mesh.Positions = positions;
        mesh.TriangleIndices = indices;
        mesh.Freeze();
        return mesh;
    }
}
