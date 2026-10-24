using System.Reflection;

namespace XRBrowser.Services;

public static class AppPaths
{
    public static string DataRoot { get; }
    public static string WebViewData { get; }
    public static string Logs { get; }
    public static string Favicons { get; }
    public static string Filters { get; }
    public static string NewTabBackgrounds { get; }
    public static string InternalAssets { get; }
    public static string DarkReaderScript { get; }
    public static string DefaultDownloads { get; }
    public static string Version { get; }

    static AppPaths()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("XR_BROWSER_DATA_DIR");
        var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));
        var preferred = !string.IsNullOrWhiteSpace(overrideRoot)
            ? overrideRoot!
            : portable
                ? Path.Combine(AppContext.BaseDirectory, "Data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XRBrowser");

        try
        {
            Directory.CreateDirectory(preferred);
            DataRoot = preferred;
        }
        catch
        {
            DataRoot = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(DataRoot);
        }

        WebViewData = Path.Combine(DataRoot, "WebView2");
        Logs = Path.Combine(DataRoot, "Logs");
        Favicons = Path.Combine(DataRoot, "Favicons");
        Filters = Path.Combine(DataRoot, "Filters");
        NewTabBackgrounds = Path.Combine(DataRoot, "NewTabBackgrounds");
        DefaultDownloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "XR Browser");
        InternalAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "Internal");
        DarkReaderScript = Path.Combine(AppContext.BaseDirectory, "Assets", "DarkReader", "darkreader.js");
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        Directory.CreateDirectory(WebViewData);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Favicons);
        Directory.CreateDirectory(Filters);
        Directory.CreateDirectory(NewTabBackgrounds);
        try { Directory.CreateDirectory(DefaultDownloads); } catch { DefaultDownloads = Path.Combine(DataRoot, "Downloads"); Directory.CreateDirectory(DefaultDownloads); }
    }

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string BookmarksFile => Path.Combine(DataRoot, "bookmarks.json");
    public static string HistoryFile => Path.Combine(DataRoot, "history.json");
    public static string SessionFile => Path.Combine(DataRoot, "session.json");
    public static string LogFile => Path.Combine(Logs, "xr-browser.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the browser down.
        }
    }
}

