namespace FiveMCleaner.App.Controls;

/// <summary>
/// O <see cref="CoreVisual"/> é <see cref="System.Windows.Media.Media3D.Viewport3D"/>
/// puro: materiais 3D não são <c>DynamicResource</c>-áveis como brushes 2D, então
/// o tema chega até eles por este sinal estático em vez de duplicar a lógica de
/// troca de paleta. <see cref="Services.ThemeManager"/> é o único escritor.
/// </summary>
public static class CoreVisualPalette
{
    private static bool isLightTheme;

    public static event EventHandler? Changed;

    public static bool IsLightTheme
    {
        get => isLightTheme;
        set
        {
            if (isLightTheme == value)
            {
                return;
            }

            isLightTheme = value;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
