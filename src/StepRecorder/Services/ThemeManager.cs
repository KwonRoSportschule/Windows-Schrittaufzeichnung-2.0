using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using StepRecorder.Core;

namespace StepRecorder.Services;

/// <summary>Switches Light/Dark resource dictionaries and colors the native title bar to match.</summary>
public static class ThemeManager
{
    private static ThemeChoice _choice = ThemeChoice.System;
    private static bool _listening;

    public static bool IsDark { get; private set; }
    public static event Action? ThemeChanged;

    public static void Apply(ThemeChoice choice)
    {
        _choice = choice;
        bool dark = choice == ThemeChoice.Dark || (choice == ThemeChoice.System && SystemPrefersDark());
        IsDark = dark;

        var dicts = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };
        if (dicts.Count > 0) dicts[0] = palette; else dicts.Add(palette);

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (_choice == ThemeChoice.System && e.Category == UserPreferenceCategory.General)
                    Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemeChoice.System));
            };
        }
        ThemeChanged?.Invoke();
    }

    /// <summary>Call once per window; colors the caption whenever the handle exists.</summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);
    }

    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int dark = IsDark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            // Windows 11: caption in the exact window background color for a seamless look.
            if (Application.Current.TryFindResource("Color.WindowBg") is Color c)
            {
                int colorRef = c.R | (c.G << 8) | (c.B << 16);
                Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
            }
        }
        catch { /* older Windows: just keep the default caption */ }
    }

    /// <summary>Read-only registry lookup of the user's app theme. Nothing is ever written.</summary>
    private static bool SystemPrefersDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch { return false; }
    }
}
