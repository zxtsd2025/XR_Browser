namespace XRBrowser.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public string SearchEngine { get; set; } = "Bing";
    public bool SearchEngineDefaultMigrated { get; set; }
    public string CustomSearchUrl { get; set; } = "https://www.google.com/search?q={0}";
    public string HomePage { get; set; } = "xr://newtab";
    public bool RestoreSession { get; set; } = true;
    public bool ShowBookmarksBar { get; set; } = true;
    public string DownloadDirectory { get; set; } = "";
    public bool AskWhereToSave { get; set; }
    public string StartupMode { get; set; } = "continue";
    public bool AutofillEnabled { get; set; } = true;
    public bool PasswordAutosaveEnabled { get; set; } = true;
    public bool JavaScriptEnabled { get; set; } = true;
    public string TrackingPrevention { get; set; } = "Balanced";
    public bool IsHardwareAccelerationEnabled { get; set; } = true;
    public double DefaultZoom { get; set; } = 1.0;
    public bool ShowHomeButton { get; set; } = true;
    public bool ConfirmCloseWithMultipleTabs { get; set; } = true;
    public bool EnableExtensions { get; set; } = true;
    public bool AdBlockEnabled { get; set; } = true;
    public bool DarkReaderEnabled { get; set; }
    public bool ShowRecentSites { get; set; }
    public string NewTabBackground { get; set; } = "";
    public bool AnimationsEnabled { get; set; } = true;
    public double AnimationSpeed { get; set; } = 1.0;
    public double SurfaceOpacity { get; set; } = 0.55;
    public bool MicaEnabled { get; set; }
    public bool GlassEnabled { get; set; } = true;
}

public sealed class SessionState
{
    public List<SessionTab> Tabs { get; set; } = new();
    public int ActiveIndex { get; set; }
    public double Width { get; set; } = 1440;
    public double Height { get; set; } = 900;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public bool Maximized { get; set; }
}

public sealed class SessionTab
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class ClosedTab
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public int Index { get; set; }
}

public sealed class OmniboxSuggestion
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Target { get; set; } = "";
    public string IconGlyph { get; set; } = "\uE774";
    public bool IsSearch { get; set; }
    public bool IsBookmark { get; set; }
}

