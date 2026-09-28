using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using TypePaste.Core.Settings;
using static TypePaste.Native.NativeMethods;

namespace TypePaste.Services;

/// <summary>Switches between the light and dark palettes and keeps window frames in sync with the theme.</summary>
internal sealed class ThemeService
{
    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);

    private ThemePreference _preference = ThemePreference.System;

    public bool IsDark { get; private set; }

    public event EventHandler? ThemeChanged;

    public void Apply(ThemePreference preference)
    {
        _preference = preference;
        var dark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => SystemUsesDarkTheme(),
        };

        // The palette is the merged dictionary that defines "TitleBarColor"; replace it in place.
        var resources = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        var index = -1;
        for (var i = 0; i < resources.Count; i++)
        {
            if (resources[i].Contains("TitleBarColor"))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            resources[index] = palette;
        }
        else
        {
            resources.Insert(0, palette);
        }

        IsDark = dark;
        ApplyNativeMenuTheme(dark);
        foreach (Window window in Application.Current.Windows)
        {
            ApplyWindowFrame(window);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-evaluates the system theme after Windows reports a settings change.</summary>
    public void OnSystemSettingChanged(string? area)
    {
        if (_preference == ThemePreference.System && area is "ImmersiveColorSet" && SystemUsesDarkTheme() != IsDark)
        {
            Apply(_preference);
        }
    }

    /// <summary>Colors the native title bar to match the current palette.</summary>
    public void ApplyWindowFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            return;
        }

        var dark = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
        }

        if (Environment.OSVersion.Version.Build >= 22000 && window.TryFindResource("TitleBarColor") is Color color)
        {
            var colorRef = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
        }
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ApplyNativeMenuTheme(bool dark)
    {
        // Undocumented but stable since Windows 10 1903: lets native menus (tray menu) follow dark mode.
        if (Environment.OSVersion.Version.Build < 18362)
        {
            return;
        }

        try
        {
            SetPreferredAppMode(dark ? 2 /* ForceDark */ : 3 /* ForceLight */);
            FlushMenuThemes();
        }
        catch (Exception)
        {
            // Not available: menus stay light.
        }
    }
}
