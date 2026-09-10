using Wpf.Ui.Controls;

namespace Ralven.App.Services;

/// <summary>
/// Chooses the closest supported system backdrop for every supported Windows
/// version. Mica is a Windows 11 material; Acrylic keeps the same layered
/// window treatment on Windows 10 version 2004 and later.
/// </summary>
public static class WindowBackdropPolicy
{
    private const int FirstWindows11Build = 22000;

    public static WindowBackdropType Current => SelectForBuild(Environment.OSVersion.Version.Build);

    internal static WindowBackdropType SelectForBuild(int build) =>
        build >= FirstWindows11Build
            ? WindowBackdropType.Mica
            : WindowBackdropType.Acrylic;
}
