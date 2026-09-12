using System.Globalization;
using System.Resources;

namespace Ralven.Windows.Actions;

public static class WindowsActionResources
{
    private static readonly ResourceManager Resources = new(
        "Ralven.Windows.Resources.Strings",
        typeof(WindowsActionResources).Assembly);

    public static WindowsActionTextResolver ForCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return (key, arguments) => string.Format(
            culture,
            Resources.GetString(key, culture)
                ?? Resources.GetString(key, CultureInfo.GetCultureInfo("en-US"))
                ?? key,
            arguments);
    }
}

internal static class WindowsActionText
{
    private static readonly AsyncLocal<WindowsActionTextResolver?> CurrentResolver = new();
    private static readonly WindowsActionTextResolver PortugueseFallback =
        WindowsActionResources.ForCulture(CultureInfo.GetCultureInfo("pt-BR"));

    public static string Format(string key, params object?[] arguments) =>
        (CurrentResolver.Value ?? PortugueseFallback)(key, arguments);

    public static IDisposable Use(WindowsActionTextResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var previous = CurrentResolver.Value;
        CurrentResolver.Value = resolver;
        return new Scope(previous);
    }

    private sealed class Scope(WindowsActionTextResolver? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            CurrentResolver.Value = previous;
            disposed = true;
        }
    }
}
