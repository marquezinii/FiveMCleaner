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
