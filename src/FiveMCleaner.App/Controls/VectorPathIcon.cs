using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Binding = System.Windows.Data.Binding;

namespace FiveMCleaner.App.Controls;

/// <summary>
/// Adapta a linguagem de ícones vetoriais próprios do app (traço, grade
/// 24x24, ver <c>Themes/Icons.xaml</c>) para os lugares que exigem um
/// <see cref="Wpf.Ui.Controls.IconElement"/> — hoje, os itens da navegação
/// lateral. O Wpf.Ui 4.3 não inclui um ícone de caminho pronto (só
/// <c>FontIcon</c>/<c>SymbolIcon</c>/<c>ImageIcon</c>).
/// </summary>
public sealed class VectorPathIcon : Wpf.Ui.Controls.IconElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(Geometry),
        typeof(VectorPathIcon));

    public VectorPathIcon()
    {
        Width = 18;
        Height = 18;
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override UIElement InitializeChildren()
    {
        var path = new Path
        {
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch
        };
        path.SetBinding(Path.DataProperty, new Binding(nameof(Data)) { Source = this });
        path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = this });
        return path;
    }
}
