using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using XRBrowser.Models;
using XRBrowser.Services;

namespace XRBrowser;

public static class InternalPageBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task HandleAsync(MainWindow window, BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (tab.Core is null) return;
        var root = default(JsonElement);
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            root = document.RootElement.Clone();
            var id = GetString(root, "id");
            var method = GetString(root, "method");
            var parameters = GetObject(root, "params");
            var result = await DispatchAsync(window, tab, method, parameters);
            Respond(tab, id, true, result, null);
        }
        catch (Exception ex)
        {
            AppPaths.Log($"Internal bridge error: {ex}");
            var id = root.ValueKind == JsonValueKind.Object ? GetString(root, "id") : "";
            Respond(tab, id, false, null, ex.Message);
        }
    }

    private static async Task<object?> DispatchAsync(MainWindow window, BrowserTab tab, string method, JsonElement p)
    {
        var settings = AppServices.Settings.Current;
        switch (method)
        {
            case "getBootstrap":
                return new
                {
                    settings = SettingsDto(settings),
                    isDark = ThemeService.IsDark,
                    isIncognito = window.IsIncognitoWindow,
                    version = AppPaths.Version,
                    webViewVersion = BrowserEnvironment.RuntimeVersion,
                    searchEngineName = UrlHelper.GetSearchEngineName(settings),
                    dataPath = AppPaths.DataRoot,
                    topSites = window.GetTopSites(8),
                    bookmarks = BookmarksDto(),
                    downloads = DownloadsDto(),
                    historyCount = AppServices.History.Items.Count,
                    bookmarkCount = AppServices.Bookmarks.Items.Count
                };

            case "getSettings":
                return SettingsDto(settings);

            case "setSetting":
            {
                var key = GetString(p, "key");
                if (!p.TryGetProperty("value", out var value))
                    throw new ArgumentException("缺少 value。");
                if (!window.ApplySetting(key, value))
                    throw new ArgumentException("未知设置项：" + key);
                return SettingsDto(AppServices.Settings.Current);
            }

            case "search":
            {
                var query = GetString(p, "query");
                if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("搜索内容为空。");
                window.NavigateTab(tab, UrlHelper.GetSearchUrl(query, settings));
                return new { navigated = true };
            }

            case "openUrl":
            {
                var url = GetString(p, "url");
                if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("网址为空。");
                var newTab = GetBool(p, "newTab");
                if (newTab) await window.CreateTabAsync(url, activate: true);
                else window.NavigateTab(tab, url);
                return new { navigated = true };
            }

            case "openInternalPage":
                await window.OpenInternalPageAsync(GetString(p, "page"));
                return new { navigated = true };

            case "getTopSites":
                return window.GetTopSites(Math.Clamp(GetInt(p, "count", 8), 1, 24));

            case "getHistory":
            {
                var query = GetString(p, "query");
                var entries = AppServices.History.Search(query, 1000);
                return entries.Select(h => new
                {
                    url = h.Url,
                    title = string.IsNullOrWhiteSpace(h.Title) ? h.Url : h.Title,
                    host = h.Host,
                    lastVisit = h.LastVisitUtc,
                    visitCount = h.VisitCount
                }).ToList();
            }

            case "deleteHistory":
            {
                var url = GetString(p, "url");
                AppServices.History.Delete(url);
                return new { deleted = true };
            }

            case "clearHistory":
                AppServices.History.Clear();
                return new { cleared = true };

            case "getBookmarks":
                return BookmarksDto();

            case "addBookmark":
            {
                var url = GetString(p, "url");
                var title = GetString(p, "title");
                if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("网址为空。");
                var bookmark = AppServices.Bookmarks.Add(title, url);
                await NotifyAsync(window, "bookmarksChanged", null);
                return new { id = bookmark.Id };
            }

            case "updateBookmark":
            {
                var id = GetString(p, "id");
                var bookmark = AppServices.Bookmarks.Items.FirstOrDefault(b => b.Id == id)
                    ?? throw new ArgumentException("书签不存在。");
                var title = GetString(p, "title");
                var url = GetString(p, "url");
                if (!string.IsNullOrWhiteSpace(title)) bookmark.Title = title;
                if (!string.IsNullOrWhiteSpace(url)) bookmark.Url = url;
                AppServices.Bookmarks.SaveAndNotify();
                await NotifyAsync(window, "bookmarksChanged", null);
                return new { updated = true };
            }

            case "removeBookmark":
            {
                var id = GetString(p, "id");
                var bookmark = AppServices.Bookmarks.Items.FirstOrDefault(b => b.Id == id);
                if (bookmark is not null) AppServices.Bookmarks.Remove(bookmark);
                await NotifyAsync(window, "bookmarksChanged", null);
                return new { removed = bookmark is not null };
            }

            case "getDownloads":
                return DownloadsDto();

            case "downloadAction":
                return DownloadAction(window, GetString(p, "id"), GetString(p, "action"));

            case "clearDownloads":
                AppServices.Downloads.ClearFinished();
                return new { cleared = true };

            case "openDownloadsFolder":
            {
                Directory.CreateDirectory(settings.DownloadDirectory);
                Process.Start(new ProcessStartInfo(settings.DownloadDirectory) { UseShellExecute = true });
                return new { opened = true };
            }

            case "chooseNewTabBackground":
                return await window.ChooseNewTabBackgroundAsync();

            case "clearNewTabBackground":
                return await window.ClearNewTabBackgroundAsync();

            case "chooseDownloadFolder":
            {
                var folder = await window.ChooseDownloadFolderAsync();
                return new { folder, settings = SettingsDto(AppServices.Settings.Current) };
            }

            case "clearBrowsingData":
            {
                var kinds = new List<string>();
                if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("kinds", out var kindsElement) &&
                    kindsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in kindsElement.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String) kinds.Add(item.GetString() ?? "");
                }
                var cleared = await window.ClearBrowsingDataAsync(kinds);
                await NotifyAsync(window, "historyChanged", null);
                return new { cleared };
            }

            case "getExtensions":
                return await window.GetExtensionsAsync();

            case "loadExtension":
                return await window.LoadUnpackedExtensionAsync();

            case "installCrx":
                return await window.InstallCrxFromDialogAsync();

            case "setExtensionEnabled":
                return new { updated = await window.SetExtensionEnabledAsync(GetString(p, "id"), GetBool(p, "enabled")) };

            case "removeExtension":
                return new { removed = await window.RemoveExtensionAsync(GetString(p, "id")) };

            case "getBrowserInfo":
                return window.GetBrowserInfo();

            case "openDevTools":
                window.OpenDevTools();
                return new { opened = true };

            case "print":
                window.PrintCurrentPage();
                return new { opened = true };

            case "toggleFullscreen":
                window.ToggleFullScreenFromPage();
                return new { fullscreen = true };

            case "reload":
                tab.Reload();
                return new { reloaded = true };

            case "newWindow":
                window.OpenNewWindowFromPage(null);
                return new { opened = true };

            case "newIncognitoWindow":
                window.OpenNewWindowFromPage(null, incognito: true);
                return new { opened = true };

            case "setDefaultBrowser":
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
                    return new { opened = true };
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("无法打开 Windows 默认应用设置。" + ex.Message);
                }

            case "showToast":
                window.ShowToastMessage(GetString(p, "message"));
                return new { shown = true };

            case "clearSiteData":
                window.ClearCurrentSiteData();
                return new { cleared = true };

            default:
                throw new ArgumentException("未知请求。" + method);
        }
    }

    private static object DownloadAction(MainWindow window, string id, string action)
    {
        var item = AppServices.Downloads.Items.FirstOrDefault(d => d.Id == id)
            ?? throw new ArgumentException("下载项不存在。");
        switch (action)
        {
            case "open": item.Open(); break;
            case "show": item.ShowInFolder(); break;
            case "pause": item.Pause(); break;
            case "resume": item.Resume(); break;
            case "cancel": item.Cancel(); break;
            case "remove": AppServices.Downloads.Remove(item); break;
            default: throw new ArgumentException("未知下载操作。" + action);
        }
        return new { ok = true };
    }

    private static List<object> BookmarksDto()
        => AppServices.Bookmarks.Items.Select(b => (object)new
        {
            id = b.Id,
            title = b.Title,
            url = b.Url,
            host = b.Host,
            added = b.AddedUtc
        }).ToList();

    private static List<object> DownloadsDto()
        => AppServices.Downloads.Items.Select(d => (object)new
        {
            id = d.Id,
            fileName = d.FileName,
            fullPath = d.FullPath,
            url = d.Url,
            stateText = d.StateText,
            isActive = d.IsActive,
            isCompleted = d.IsCompleted,
            progress = d.Progress,
            progressText = d.ProgressText,
            speed = d.SpeedText,
            canOpen = d.CanOpen,
            canShowInFolder = d.CanShowInFolder,
            canPause = d.CanPause,
            canResume = d.CanResume,
            started = d.StartedUtc
        }).ToList();

    private static object SettingsDto(AppSettings s) => new
    {
        theme = s.Theme,
        resolvedTheme = ThemeService.IsDark ? "Dark" : "Light",
        isDark = ThemeService.IsDark,
        searchEngine = s.SearchEngine,
        searchEngineName = UrlHelper.GetSearchEngineName(s),
        customSearchUrl = s.CustomSearchUrl,
        homePage = s.HomePage,
        restoreSession = s.RestoreSession,
        showBookmarksBar = s.ShowBookmarksBar,
        downloadDirectory = s.DownloadDirectory,
        askWhereToSave = s.AskWhereToSave,
        startupMode = s.StartupMode,
        autofillEnabled = s.AutofillEnabled,
        passwordAutosaveEnabled = s.PasswordAutosaveEnabled,
        javaScriptEnabled = s.JavaScriptEnabled,
        trackingPrevention = s.TrackingPrevention,
        defaultZoom = s.DefaultZoom,
        showHomeButton = s.ShowHomeButton,
        enableExtensions = s.EnableExtensions,
        adBlockEnabled = s.AdBlockEnabled,
        darkReaderEnabled = s.DarkReaderEnabled,
        showRecentSites = s.ShowRecentSites,
        newTabBackground = s.NewTabBackground,
        animationsEnabled = s.AnimationsEnabled,
        animationSpeed = s.AnimationSpeed,
        surfaceOpacity = s.SurfaceOpacity,
        micaEnabled = s.MicaEnabled,
        glassEnabled = s.GlassEnabled,
        confirmCloseWithMultipleTabs = s.ConfirmCloseWithMultipleTabs
    };

    public static Task NotifyAsync(MainWindow window, string name, object? data)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { type = "event", name, data }, JsonOptions);
            foreach (var tab in window.Tabs)
            {
                if (tab.Core is null || !UrlHelper.IsInternalUrl(tab.Core.Source)) continue;
                try { tab.Core.PostWebMessageAsJson(payload); } catch { }
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("Bridge notify failed: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    private static void Respond(BrowserTab tab, string id, bool ok, object? result, string? error)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { type = "response", id, ok, result, error }, JsonOptions);
            tab.Core?.PostWebMessageAsJson(payload);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Bridge response failed: " + ex.Message);
        }
    }

    private static JsonElement GetObject(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Object ? value : default;

    private static string GetString(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool GetBool(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.True;

    private static int GetInt(JsonElement root, string name, int fallback)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : fallback;
}

