using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace FiveMCleaner.App.Services;

public sealed class ThemeManager : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BackgroundBrush"] = "#0A0B0D",
            ["SurfaceBrush"] = "#111317",
            ["SurfaceRaisedBrush"] = "#17191E",
            ["SurfaceHoverBrush"] = "#1D2026",
            ["BorderBrush"] = "#282B32",
            ["BorderSoftBrush"] = "#1F2228",
            ["TextBrush"] = "#F4F5F7",
            ["TextMutedBrush"] = "#9A9FA9",
            ["TextSubtleBrush"] = "#676C76",
            ["OrangeBrush"] = "#FF7A18",
            ["OrangeLightBrush"] = "#FFAA62",
            ["GreenBrush"] = "#37C889",
            ["BlueBrush"] = "#62A8FF",
            ["YellowBrush"] = "#EAB64D",
            ["RedBrush"] = "#F26B6B",
            ["ChromeBrush"] = "#0E1013",
            ["SidebarBrush"] = "#0D0F12",
            ["PanelBrush"] = "#101216",
            ["InsetBrush"] = "#0C0E11",
            ["ChipBrush"] = "#24272D",
            ["StrongBorderBrush"] = "#2A2D33",
            ["RingBrush"] = "#383B42",
            ["ToolTipBackgroundBrush"] = "#F0222429",
            ["WindowHoverBrush"] = "#26292F",
            ["WindowPressedBrush"] = "#33363D",
            ["ToggleTrackBrush"] = "#34373E",
            ["ToggleTrackBorderBrush"] = "#484B53",
            ["ToggleThumbBrush"] = "#D9DBDF",
            ["ScrollThumbBrush"] = "#50545D",
            ["ScrollThumbHoverBrush"] = "#737985"
        };

    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BackgroundBrush"] = "#F4F5F7",
            ["SurfaceBrush"] = "#FFFFFF",
            ["SurfaceRaisedBrush"] = "#F0F2F5",
            ["SurfaceHoverBrush"] = "#E7EBF0",
            ["BorderBrush"] = "#C9CED6",
            ["BorderSoftBrush"] = "#DEE2E8",
            ["TextBrush"] = "#171A1F",
            ["TextMutedBrush"] = "#5E6470",
            ["TextSubtleBrush"] = "#858B96",
            ["OrangeBrush"] = "#E85D04",
            ["OrangeLightBrush"] = "#B84500",
            ["GreenBrush"] = "#16785B",
            ["BlueBrush"] = "#2367AE",
            ["YellowBrush"] = "#8A5B00",
            ["RedBrush"] = "#C83B43",
            ["ChromeBrush"] = "#FFFFFF",
            ["SidebarBrush"] = "#F8F9FB",
            ["PanelBrush"] = "#FFFFFF",
            ["InsetBrush"] = "#F5F6F8",
            ["ChipBrush"] = "#ECEFF3",
            ["StrongBorderBrush"] = "#C9CED6",
            ["RingBrush"] = "#D1D5DB",
            ["ToolTipBackgroundBrush"] = "#F7FFFFFF",
            ["WindowHoverBrush"] = "#E9ECF0",
            ["WindowPressedBrush"] = "#DCE1E7",
            ["ToggleTrackBrush"] = "#D7DBE1",
            ["ToggleTrackBorderBrush"] = "#BFC5CE",
            ["ToggleThumbBrush"] = "#FFFFFF",
            ["ScrollThumbBrush"] = "#AAB1BC",
            ["ScrollThumbHoverBrush"] = "#868E9A"
        };

    private AppThemePreference preference = AppThemePreference.System;
    private bool disposed;

    public ThemeManager()
    {
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    public AppThemePreference Preference => preference;

    public void Apply(AppThemePreference value)
    {
        if (!Enum.IsDefined(value))
        {
            value = AppThemePreference.System;
        }

        preference = value;
        ApplyEffectiveTheme(value == AppThemePreference.Light
            || value == AppThemePreference.System && IsSystemLightTheme());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    private void SystemParameters_StaticPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (disposed || preference != AppThemePreference.System)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() => Apply(AppThemePreference.System));
    }

    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (disposed || preference != AppThemePreference.System)
        {
            return;
        }

        // Windows does not consistently raise a WPF SystemParameters change
        // for AppsUseLightTheme. UserPreferenceChanged covers the shell/theme
        // notification used by Windows 10 and 11.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            _ = dispatcher.BeginInvoke(() => Apply(AppThemePreference.System));
        }
    }

    private void ApplyEffectiveTheme(bool useLightTheme)
    {
        var palette = useLightTheme ? LightPalette : DarkPalette;
        foreach (var entry in palette)
        {
            SetBrushColor(entry.Key, entry.Value);
        }

        var colors = useLightTheme
            ? new[] { "#FFF7F1", "#FFFFFF", "#F4F5F7" }
            : new[] { "#1B1715", "#141519", "#101115" };
        System.Windows.Application.Current.Resources["HeroGradientBrush"] = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(ParseColor(colors[0]), 0),
                new(ParseColor(colors[1]), 0.56),
                new(ParseColor(colors[2]), 1)
            },
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1));

        ApplyOverviewSurfaces(useLightTheme);

        // WPF UI uses the application accent for FluentWindow chrome as well.
        // Keep window borders neutral; orange remains reserved for selected actions.
        var accent = useLightTheme ? ParseColor("#C9CED6") : ParseColor("#2A2D33");
        var wpfUiTheme = useLightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationAccentColorManager.Apply(accent, wpfUiTheme, systemGlassColor: false, systemAccentColor: false);
        ApplicationThemeManager.Apply(wpfUiTheme, updateAccent: false);
    }

    /// <summary>
    /// As superfícies em vidro da Visão geral são gradientes, e não cores
    /// sólidas, então não cabem no mapa de <see cref="SetBrushColor"/>. Elas
    /// seguem o mesmo contrato do hero: laranja apenas como brilho de destaque.
    /// </summary>
    private static void ApplyOverviewSurfaces(bool useLightTheme)
    {
        SetLinearGradient(
            "GlassSurfaceBrush",
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0.35, 1),
            useLightTheme
                ? [("#FFFFFF", 0), ("#FAFBFC", 0.55), ("#F2F4F7", 1)]
                : [("#15171C", 0), ("#111318", 0.55), ("#0D0F13", 1)]);

        SetLinearGradient(
            "GlassBorderBrush",
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 1),
            useLightTheme
                ? [("#D8DDE4", 0), ("#E2E6EC", 0.5), ("#E8EBF0", 1)]
                : [("#2E323B", 0), ("#1E2128", 0.5), ("#191C22", 1)]);

        SetLinearGradient(
            "TileSurfaceBrush",
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 1),
            useLightTheme
                ? [("#FFFFFF", 0), ("#F4F6F9", 1)]
                : [("#14161B", 0), ("#0E1014", 1)]);

        SetLinearGradient(
            "HeroBorderBrush",
            new System.Windows.Point(1, 0),
            new System.Windows.Point(0, 1),
            useLightTheme
                ? [("#F0C79E", 0), ("#DDE1E7", 0.4), ("#E6E9EE", 1)]
                : [("#5A3A1E", 0), ("#25282F", 0.4), ("#1B1E24", 1)]);

        (string Color, double Offset)[] heroStops = useLightTheme
            ? [("#FFF2E4", 0), ("#FFFFFF", 0.42), ("#F6F7F9", 1)]
            : [("#2A1D14", 0), ("#17171B", 0.42), ("#0E1013", 1)];
        System.Windows.Application.Current.Resources["HeroSurfaceBrush"] = new RadialGradientBrush(
            BuildStops(heroStops))
        {
            GradientOrigin = new System.Windows.Point(0.86, 0.12),
            Center = new System.Windows.Point(0.86, 0.12),
            RadiusX = 1.15,
            RadiusY = 1.5
        };
    }

    private static void SetLinearGradient(
        string resourceKey,
        System.Windows.Point start,
        System.Windows.Point end,
        (string Color, double Offset)[] stops)
    {
        System.Windows.Application.Current.Resources[resourceKey] =
            new LinearGradientBrush(BuildStops(stops), start, end);
    }

    private static GradientStopCollection BuildStops((string Color, double Offset)[] stops)
    {
        var collection = new GradientStopCollection(stops.Length);
        foreach (var stop in stops)
        {
            collection.Add(new GradientStop(ParseColor(stop.Color), stop.Offset));
        }

        return collection;
    }

    private static void SetBrushColor(string resourceKey, string value)
    {
        System.Windows.Application.Current.Resources[resourceKey] = new SolidColorBrush(ParseColor(value));
    }

    private static System.Windows.Media.Color ParseColor(string value)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException)
        {
            return false;
        }
    }
}
