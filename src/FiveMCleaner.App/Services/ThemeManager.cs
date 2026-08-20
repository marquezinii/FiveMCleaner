using System.IO;
using System.Windows;
using FiveMCleaner.App.Controls;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace FiveMCleaner.App.Services;

/// <summary>
/// Fonte única de verdade para cor: troca o dicionário de tokens semânticos
/// inteiro (Colors.Dark.xaml/Colors.Light.xaml) em vez de sobrescrever
/// chave a chave. Qualquer brush novo nasce nesses dois arquivos, nunca aqui.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string DarkTokensUri = "Themes/Tokens/Colors.Dark.xaml";
    private const string LightTokensUri = "Themes/Tokens/Colors.Light.xaml";

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

    private static void ApplyEffectiveTheme(bool useLightTheme)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var tokensUri = new Uri(useLightTheme ? LightTokensUri : DarkTokensUri, UriKind.Relative);
        var merged = app.Resources.MergedDictionaries;

        ResourceDictionary? existing = null;
        foreach (var dictionary in merged)
        {
            if (dictionary.Source is not null
                && (dictionary.Source.OriginalString == DarkTokensUri
                    || dictionary.Source.OriginalString == LightTokensUri))
            {
                existing = dictionary;
                break;
            }
        }

        var replacement = new ResourceDictionary { Source = tokensUri };
        if (existing is not null)
        {
            var index = merged.IndexOf(existing);
            merged.RemoveAt(index);
            merged.Insert(index, replacement);
        }
        else
        {
            // Primeira aplicação: os tokens de cor devem carregar antes de
            // Typography/Surfaces/Controls, que os consomem via DynamicResource.
            merged.Insert(0, replacement);
        }

        // WPF UI deriva sua própria família de acento (indicador do rail de
        // navegação, realce de seleção, chrome da FluentWindow) desta cor.
        // Na direção "Câmara Âmbar" ela precisa ser o mesmo âmbar de
        // AccentBrush nos tokens: se os dois divergirem, o indicador da
        // navegação acende numa cor que não existe em nenhum outro lugar da
        // interface. Os valores abaixo são exatamente AccentBrush de
        // Colors.Dark.xaml e Colors.Light.xaml.
        var accent = useLightTheme ? ParseColor("#C3540A") : ParseColor("#E8720F");
        var wpfUiTheme = useLightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationAccentColorManager.Apply(accent, wpfUiTheme, systemGlassColor: false, systemAccentColor: false);
        ApplicationThemeManager.Apply(wpfUiTheme, updateAccent: false);
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
