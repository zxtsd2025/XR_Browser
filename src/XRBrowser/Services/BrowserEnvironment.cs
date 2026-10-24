using Microsoft.Web.WebView2.Core;
using XRBrowser.Models;

namespace XRBrowser.Services;

public static class BrowserEnvironment
{
    private static Task<CoreWebView2Environment>? _environmentTask;
    private static string? _version;

    public static string RuntimeVersion => _version ?? "未知";

    public static Task<CoreWebView2Environment> GetAsync(AppSettings settings)
    {
        if (_environmentTask is not null) return _environmentTask;

        var options = new CoreWebView2EnvironmentOptions
        {
            Language = "zh-CN",
            AreBrowserExtensionsEnabled = settings.EnableExtensions,
            EnableTrackingPrevention = true,
            ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay,
            IsCustomCrashReportingEnabled = false
        };

        _environmentTask = CreateAsync(options);
        return _environmentTask;
    }

    private static async Task<CoreWebView2Environment> CreateAsync(CoreWebView2EnvironmentOptions options)
    {
        AppPaths.Log($"Creating WebView2 environment in {AppPaths.WebViewData}");
        var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData, options);
        _version = environment.BrowserVersionString;
        AppPaths.Log($"WebView2 runtime version {_version}");
        return environment;
    }

    public static string? GetRuntimeVersionOrNull()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch { return null; }
    }
}
