using XRBrowser.Models;

namespace XRBrowser.Services;

public sealed class SettingsService
{
    private readonly JsonStore<AppSettings> _store = new(AppPaths.SettingsFile);
    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public SettingsService()
    {
        Current = _store.Load();
        Normalize(Current);
        if (!Current.SearchEngineDefaultMigrated)
        {
            if (Current.SearchEngine.Equals("Google", StringComparison.OrdinalIgnoreCase))
                Current.SearchEngine = "Bing";
            Current.SearchEngineDefaultMigrated = true;
            _store.Save(Current);
        }
    }

    public void Normalize(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Theme)) s.Theme = "System";
        if (s.Theme is not ("System" or "Light" or "Dark")) s.Theme = "System";
        if (string.IsNullOrWhiteSpace(s.SearchEngine)) s.SearchEngine = "Bing";
        if (string.IsNullOrWhiteSpace(s.CustomSearchUrl) || !s.CustomSearchUrl.Contains("{0}"))
            s.CustomSearchUrl = "https://www.google.com/search?q={0}";
        if (string.IsNullOrWhiteSpace(s.HomePage)) s.HomePage = "xr://newtab";
        if (string.IsNullOrWhiteSpace(s.DownloadDirectory))
            s.DownloadDirectory = AppPaths.DefaultDownloads;
        try { Directory.CreateDirectory(s.DownloadDirectory); }
        catch { s.DownloadDirectory = AppPaths.DefaultDownloads; }
        if (s.StartupMode is not ("continue" or "newtab" or "home")) s.StartupMode = "continue";
        if (s.TrackingPrevention is not ("Off" or "Basic" or "Balanced" or "Strict"))
            s.TrackingPrevention = "Balanced";
        if (s.DefaultZoom < 0.25 || s.DefaultZoom > 5.0) s.DefaultZoom = 1.0;
        if (s.AnimationSpeed < 0.5 || s.AnimationSpeed > 2.0) s.AnimationSpeed = 1.0;
        if (s.SurfaceOpacity < 0.25 || s.SurfaceOpacity > 1.0) s.SurfaceOpacity = 0.55;
    }

    public void Save()
    {
        Normalize(Current);
        _store.Save(Current);
    }

    public void RaiseChanged()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Set(string key, System.Text.Json.JsonElement value)
    {
        bool GetBool(bool fallback) => value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => fallback
        };
        double GetDouble(double fallback) => value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDouble(out var d) ? d : fallback;
        string GetString(string fallback) => value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? fallback : fallback;

        switch (key)
        {
            case "theme": Current.Theme = GetString(Current.Theme); break;
            case "searchEngine": Current.SearchEngine = GetString(Current.SearchEngine); break;
            case "customSearchUrl": Current.CustomSearchUrl = GetString(Current.CustomSearchUrl); break;
            case "homePage": Current.HomePage = GetString(Current.HomePage); break;
            case "restoreSession": Current.RestoreSession = GetBool(Current.RestoreSession); break;
            case "showBookmarksBar": Current.ShowBookmarksBar = GetBool(Current.ShowBookmarksBar); break;
            case "downloadDirectory": Current.DownloadDirectory = GetString(Current.DownloadDirectory); break;
            case "askWhereToSave": Current.AskWhereToSave = GetBool(Current.AskWhereToSave); break;
            case "startupMode": Current.StartupMode = GetString(Current.StartupMode); break;
            case "autofillEnabled": Current.AutofillEnabled = GetBool(Current.AutofillEnabled); break;
            case "passwordAutosaveEnabled": Current.PasswordAutosaveEnabled = GetBool(Current.PasswordAutosaveEnabled); break;
            case "javaScriptEnabled": Current.JavaScriptEnabled = GetBool(Current.JavaScriptEnabled); break;
            case "trackingPrevention": Current.TrackingPrevention = GetString(Current.TrackingPrevention); break;
            case "isHardwareAccelerationEnabled": Current.IsHardwareAccelerationEnabled = GetBool(Current.IsHardwareAccelerationEnabled); break;
            case "defaultZoom": Current.DefaultZoom = Math.Clamp(GetDouble(Current.DefaultZoom), 0.25, 5); break;
            case "showHomeButton": Current.ShowHomeButton = GetBool(Current.ShowHomeButton); break;
            case "confirmCloseWithMultipleTabs": Current.ConfirmCloseWithMultipleTabs = GetBool(Current.ConfirmCloseWithMultipleTabs); break;
            case "enableExtensions": Current.EnableExtensions = GetBool(Current.EnableExtensions); break;
            case "adBlockEnabled": Current.AdBlockEnabled = GetBool(Current.AdBlockEnabled); break;
            case "darkReaderEnabled": Current.DarkReaderEnabled = GetBool(Current.DarkReaderEnabled); break;
            case "showRecentSites": Current.ShowRecentSites = GetBool(Current.ShowRecentSites); break;
            case "animationsEnabled": Current.AnimationsEnabled = GetBool(Current.AnimationsEnabled); break;
            case "animationSpeed": Current.AnimationSpeed = Math.Clamp(GetDouble(Current.AnimationSpeed), 0.5, 2.0); break;
            case "surfaceOpacity": Current.SurfaceOpacity = Math.Clamp(GetDouble(Current.SurfaceOpacity), 0.25, 1.0); break;
            case "micaEnabled": Current.MicaEnabled = GetBool(Current.MicaEnabled); break;
            case "glassEnabled": Current.GlassEnabled = GetBool(Current.GlassEnabled); break;
            default: return false;
        }
        Normalize(Current);
        return true;
    }
}

