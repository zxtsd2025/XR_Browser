using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using XRBrowser.Services;
using DrawingColor = System.Drawing.Color;

namespace XRBrowser.Models;

public sealed class BrowserTab : INotifyPropertyChanged, IDisposable
{
    private readonly bool _isIncognito;
    private string _title = "新标签页";
    private string _url = "";
    private ImageSource? _favicon;
    private bool _isLoading;
    private bool _canGoBack;
    private bool _canGoForward;
    private bool _isActive;
    private bool _isMuted;
    private bool _isPlayingAudio;
    private bool _isBookmarked;
    private bool _isCrashed;
    private bool _isInitialized;
    private bool _disposed;
    private double _zoomFactor = 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? FaviconUpdated;
    public event EventHandler? FullScreenChanged;

    public Guid Id { get; } = Guid.NewGuid();
    public WebView2 WebView { get; }
    public CoreWebView2? Core => WebView.CoreWebView2;
    public CoreWebView2ContextMenuItem? AdBlockMenuItem { get; set; }
    public CoreWebView2ContextMenuItem? DarkReaderMenuItem { get; set; }
    public bool DarkReaderInstalled { get; set; }
    public bool IsIncognito => _isIncognito;
    public bool IsInitialized => _isInitialized;
    public bool IsDisposed => _disposed;

    public string Title
    {
        get => _title;
        set => Set(ref _title, string.IsNullOrWhiteSpace(value) ? "无标题" : value);
    }

    public string Url
    {
        get => _url;
        set
        {
            if (!Set(ref _url, value)) return;
            Raise(nameof(DisplayUrl));
            Raise(nameof(IsNewTabPage));
            Raise(nameof(SecurityState));
            Raise(nameof(ToolTip));
        }
    }

    public string DisplayUrl => UrlHelper.GetFullDisplayUrl(Url);
    public bool IsNewTabPage => UrlHelper.IsNewTabUrl(Url);
    public SecurityState SecurityState => UrlHelper.GetSecurityState(Url);

    public string ToolTip => string.IsNullOrWhiteSpace(DisplayUrl) ? Title : $"{Title}\n{DisplayUrl}";

    public ImageSource? Favicon
    {
        get => _favicon;
        set => Set(ref _favicon, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => Set(ref _isLoading, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        set => Set(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        set => Set(ref _canGoForward, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => Set(ref _isMuted, value);
    }

    public bool IsPlayingAudio
    {
        get => _isPlayingAudio;
        set => Set(ref _isPlayingAudio, value);
    }

    public bool IsBookmarked
    {
        get => _isBookmarked;
        set => Set(ref _isBookmarked, value);
    }

    public bool IsCrashed
    {
        get => _isCrashed;
        set => Set(ref _isCrashed, value);
    }

    public double ZoomFactor
    {
        get => _zoomFactor;
        set => Set(ref _zoomFactor, value);
    }

    public string ZoomPercent => $"{Math.Round(ZoomFactor * 100)}%";

    public BrowserTab(bool isIncognito)
    {
        _isIncognito = isIncognito;
        WebView = new WebView2
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            Focusable = true,
            AllowExternalDrop = true,
            DefaultBackgroundColor = DrawingColor.FromArgb(255, 255, 255, 255),
            Visibility = System.Windows.Visibility.Visible
        };
    }

    public async Task InitializeAsync(CoreWebView2Environment environment, XRBrowser.Models.AppSettings settings)
    {
        await WaitForLoadedAsync(WebView);
        AppPaths.Log($"Ensuring WebView2 (loaded={WebView.IsLoaded}, actual={WebView.ActualWidth}x{WebView.ActualHeight})");
        if (_isIncognito)
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await WebView.EnsureCoreWebView2Async(environment, options);
        }
        else
        {
            await WebView.EnsureCoreWebView2Async(environment);
        }
        AppPaths.Log("WebView2 controller created");
        var core = WebView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 initialization failed.");

        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsPinchZoomEnabled = true;
        core.Settings.IsSwipeNavigationEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = settings.AutofillEnabled;
        core.Settings.IsPasswordAutosaveEnabled = settings.PasswordAutosaveEnabled;
        core.Settings.IsScriptEnabled = settings.JavaScriptEnabled;
        core.Settings.IsWebMessageEnabled = true;

        try
        {
            core.SetVirtualHostNameToFolderMapping(
                UrlHelper.InternalHost,
                AppPaths.InternalAssets,
                CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(
                UrlHelper.BackgroundHost,
                AppPaths.NewTabBackgrounds,
                CoreWebView2HostResourceAccessKind.Allow);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Virtual host mapping failed: " + ex);
        }

        try
        {
            core.Profile.PreferredTrackingPreventionLevel = settings.TrackingPrevention switch
            {
                "Off" => CoreWebView2TrackingPreventionLevel.None,
                "Basic" => CoreWebView2TrackingPreventionLevel.Basic,
                "Strict" => CoreWebView2TrackingPreventionLevel.Strict,
                _ => CoreWebView2TrackingPreventionLevel.Balanced
            };
        }
        catch { }

        _zoomFactor = Math.Clamp(settings.DefaultZoom, 0.25, 5);
        WebView.ZoomFactor = _zoomFactor;

        core.SourceChanged += OnSourceChanged;
        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.HistoryChanged += OnHistoryChanged;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.FaviconChanged += OnFaviconChanged;
        core.IsDocumentPlayingAudioChanged += OnAudioChanged;
        core.ContainsFullScreenElementChanged += OnFullScreenChanged;

        _isInitialized = true;
        Raise(nameof(IsInitialized));
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var core = Core;
        if (core is null) return;
        Url = core.Source;
        CanGoBack = core.CanGoBack;
        CanGoForward = core.CanGoForward;
        if (UrlHelper.IsNewTabUrl(core.Source)) Title = "新标签页";
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        var core = Core;
        if (core is null) return;
        if (UrlHelper.IsNewTabUrl(core.Source)) return;
        if (!string.IsNullOrWhiteSpace(core.DocumentTitle)) Title = core.DocumentTitle;
        else if (!string.IsNullOrWhiteSpace(UrlHelper.GetHost(core.Source))) Title = UrlHelper.GetHost(core.Source);
    }

    private void OnHistoryChanged(object? sender, object e)
    {
        var core = Core;
        if (core is null) return;
        CanGoBack = core.CanGoBack;
        CanGoForward = core.CanGoForward;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        IsLoading = true;
        IsCrashed = false;
        Favicon = null;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var core = Core;
        IsLoading = false;
        if (core is not null)
        {
            Url = core.Source;
            CanGoBack = core.CanGoBack;
            CanGoForward = core.CanGoForward;
        }

        if (!e.IsSuccess)
        {
            if (e.WebErrorStatus is CoreWebView2WebErrorStatus.ConnectionAborted
                or CoreWebView2WebErrorStatus.OperationCanceled)
                return;
            // Chromium's own error page remains visible; a crashed renderer is handled by ProcessFailed.
        }
    }

    private void OnFaviconChanged(object? sender, object e)
    {
        FaviconUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void OnAudioChanged(object? sender, object e)
    {
        var core = Core;
        if (core is null) return;
        IsPlayingAudio = core.IsDocumentPlayingAudio;
        IsMuted = core.IsMuted;
    }

    private void OnFullScreenChanged(object? sender, object e)
        => FullScreenChanged?.Invoke(this, EventArgs.Empty);

    private static async Task WaitForLoadedAsync(FrameworkElement element)
    {
        if (element.IsLoaded) return;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            element.Loaded -= handler;
            tcs.TrySetResult();
        };
        element.Loaded += handler;
        if (element.Dispatcher.CheckAccess())
            await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        element.Loaded -= handler;
        if (completed != tcs.Task || !element.IsLoaded)
            throw new InvalidOperationException("WebView host window was not created in time.");
    }

    public void Navigate(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || Core is null) return;
        if (UrlHelper.IsInternalInput(url))
            url = UrlHelper.ToInternalUrl(url);
        try { Core.Navigate(url); }
        catch (Exception ex) { AppPaths.Log("Navigate failed: " + ex.Message); }
    }

    public void GoBack() { try { if (Core?.CanGoBack == true) Core.GoBack(); } catch { } }
    public void GoForward() { try { if (Core?.CanGoForward == true) Core.GoForward(); } catch { } }
    public void Reload(bool ignoreCache = false)
    {
        try
        {
            if (Core is null) return;
            if (ignoreCache) Core.Reload();
            else Core.Reload();
        }
        catch { }
    }

    public void Stop() { try { Core?.Stop(); } catch { } }

    public void ToggleMute()
    {
        if (Core is null) return;
        Core.IsMuted = !Core.IsMuted;
        IsMuted = Core.IsMuted;
    }

    public void SetMuted(bool muted)
    {
        if (Core is null) return;
        Core.IsMuted = muted;
        IsMuted = muted;
    }

    public void SetZoom(double factor)
    {
        factor = Math.Clamp(factor, 0.25, 5.0);
        WebView.ZoomFactor = factor;
        ZoomFactor = factor;
    }

    public void SetBackground(Color color)
    {
        WebView.DefaultBackgroundColor = DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        WebView.Visibility = active ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Core is not null)
            {
                Core.SourceChanged -= OnSourceChanged;
                Core.DocumentTitleChanged -= OnDocumentTitleChanged;
                Core.HistoryChanged -= OnHistoryChanged;
                Core.NavigationStarting -= OnNavigationStarting;
                Core.NavigationCompleted -= OnNavigationCompleted;
                Core.FaviconChanged -= OnFaviconChanged;
                Core.IsDocumentPlayingAudioChanged -= OnAudioChanged;
                Core.ContainsFullScreenElementChanged -= OnFullScreenChanged;
            }
        }
        catch { }
        try { WebView.Dispose(); } catch { }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

