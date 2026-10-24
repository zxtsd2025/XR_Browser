namespace XRBrowser.Services;

public static class AppServices
{
    public static SettingsService Settings { get; private set; } = null!;
    public static BookmarkService Bookmarks { get; private set; } = null!;
    public static HistoryService History { get; private set; } = null!;
    public static DownloadService Downloads { get; private set; } = null!;
    public static SessionService Sessions { get; private set; } = null!;
    public static FaviconService Favicons { get; private set; } = null!;
    public static AdBlockService AdBlock { get; private set; } = null!;
    public static bool IsShuttingDown { get; set; }
    public static bool Initialized { get; private set; }

    public static void Initialize()
    {
        if (Initialized) return;
        Settings = new SettingsService();
        Bookmarks = new BookmarkService();
        History = new HistoryService();
        Downloads = new DownloadService();
        Sessions = new SessionService();
        Favicons = new FaviconService();
        AdBlock = new AdBlockService(Settings);
        Initialized = true;
    }

    public static void Shutdown()
    {
        if (!Initialized) return;
        Settings.Save();
        Bookmarks.SaveAndNotify();
        History.SaveNow();
        History.Dispose();
        Sessions.Flush();
    }
}
