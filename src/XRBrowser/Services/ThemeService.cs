using System.Windows;
using Microsoft.Win32;

namespace XRBrowser.Services;

public static class ThemeService
{
    public static bool IsDark { get; private set; }
    public static event EventHandler? ThemeChanged;

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(string theme)
    {
        IsDark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark()
        };

        var app = Application.Current;
        if (app is null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString ?? "";
            if (source.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        var uri = new Uri($"pack://application:,,,/XR;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml");
        var dictionary = new ResourceDictionary { Source = uri };
        dictionaries.Insert(0, dictionary);

        foreach (Window window in app.Windows)
        {
            if (window.IsLoaded) NativeMethods.ApplyDarkFrame(window, IsDark);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
