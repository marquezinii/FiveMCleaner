using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FiveMCleaner.App;

/// <summary>Resolves a theme brush resource key (e.g. "GreenBrush") to its current DynamicResource value.</summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrEmpty(key))
        {
            return DependencyProperty.UnsetValue;
        }

        return System.Windows.Application.Current.TryFindResource(key) is System.Windows.Media.Brush brush
            ? brush
            : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Resolves a vector icon resource key (e.g. "IconCpu", declared in
/// <c>Themes/Icons.xaml</c>) to its <see cref="System.Windows.Media.Geometry"/>.
/// Lets a view model name an icon without referencing WPF drawing types.
/// </summary>
public sealed class GeometryKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrEmpty(key))
        {
            return DependencyProperty.UnsetValue;
        }

        return System.Windows.Application.Current.TryFindResource(key) is System.Windows.Media.Geometry geometry
            ? geometry
            : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Largura em pixels de um indicador de progresso proporcional: value/maximum
/// do <see cref="System.Windows.Controls.ProgressBar"/> multiplicado pela
/// largura real do trilho. Usado pelo <c>ProgressRailStyle</c> em vez do
/// <c>ScaleTransform</c> padrão do WPF, que a interface Fluent deste app não
/// usa em nenhuma lista ou indicador.
/// </summary>
public sealed class PercentWidthConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [double value, double maximum, double trackWidth]
            || maximum <= 0
            || double.IsNaN(trackWidth))
        {
            return 0d;
        }

        var ratio = Math.Clamp(value / maximum, 0, 1);
        return trackWidth * ratio;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Collapses an element when the bound string is null or empty.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Visible only when the bound collection is empty. Used for the empty-state
/// composition shown instead of an item list, never the other way around
/// (never treat an empty collection as a success state).
/// </summary>
public sealed class CollectionEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value switch
        {
            int count => count == 0,
            System.Collections.ICollection collection => collection.Count == 0,
            null => true,
            _ => false
        };

        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            isEmpty = !isEmpty;
        }

        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
