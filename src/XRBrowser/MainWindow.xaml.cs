using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using XRBrowser.Dialogs;
using XRBrowser.Models;
using XRBrowser.Services;

namespace XRBrowser;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly bool _isPrimary;
    private readonly bool _isIncognito;
    private readonly string? _pendingInitialUrl;
    private readonly List<ClosedTab> _closedTabs = new();

    private readonly DispatcherTimer _suggestionTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private readonly DispatcherTimer _addressBlurTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _findTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };

    private CoreWebView2Environment? _environment;
    private BrowserTab? _activeTab;
    private SessionState? _sessionToRestore;
    private bool _restoreMaximized;
    private bool _loaded;
    private bool _tabsReady;
    private bool _closing;
    private bool _fullscreen;
    private bool _addressFocused;
    private bool _suppressAddress;
    private int _findVersion;
    private string _appliedTheme = "";
    private bool _lastAdBlockEnabled;
    private bool _lastDarkReaderEnabled;
    private bool _lastMicaEnabled;
    private bool _lastGlassEnabled;
    private double _lastSurfaceOpacity;
    private static string? _darkReaderSource;
    private bool _isExtensionsLoading;
    private string _extensionsError = "";
    private WindowState _preFullscreenState = WindowState.Normal;
    private Rect _preFullscreenRect = Rect.Empty;
    private Point _tabDragStart;
    private BrowserTab? _tabDragTab;
    private bool _tabDragArmed;
    private bool _tabDragging;
    private readonly HashSet<BrowserTab> _closingTabs = new();
    private readonly Dictionary<FrameworkElement, SpringAnimation> _springs = new();
    private readonly HashSet<Popup> _closingPopups = new();
    private SpringAnimation? _tabIndicatorSpring;
    private bool _tabIndicatorReady;
    private bool _tabIndicatorPending;

    public static readonly DependencyProperty TabWidthProperty =
        DependencyProperty.Register(nameof(TabWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(220d));

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    public ObservableCollection<OmniboxSuggestion> Suggestions { get; } = new();
    public ObservableCollection<Bookmark> Bookmarks => AppServices.Bookmarks.Items;
    public ObservableCollection<DownloadItem> Downloads => AppServices.Downloads.Items;
    public ObservableCollection<ExtensionItem> Extensions { get; } = new();

    public bool AdBlockEnabled => Settings.AdBlockEnabled;
    public bool DarkReaderEnabled => Settings.DarkReaderEnabled;

    public bool IsExtensionsLoading
    {
        get => _isExtensionsLoading;
        private set
        {
            if (_isExtensionsLoading == value) return;
            _isExtensionsLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExtensionsEmptyVisibility));
        }
    }

    public string ExtensionsError
    {
        get => _extensionsError;
        private set
        {
            if (_extensionsError == value) return;
            _extensionsError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExtensionsEmptyVisibility));
        }
    }

    public bool ExtensionsCanInstall => Settings.EnableExtensions;

    public Visibility ExtensionsDisabledVisibility =>
        Settings.EnableExtensions ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ExtensionsEmptyVisibility =>
        Settings.EnableExtensions && !IsExtensionsLoading && Extensions.Count == 0 && string.IsNullOrEmpty(ExtensionsError)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public MainWindow(string? initialUrl, bool incognito, bool isPrimary)
    {
        InitializeComponent();
        ConfigurePopupAnimations();
        _pendingInitialUrl = initialUrl;
        _isIncognito = incognito;
        _isPrimary = isPrimary;

        DataContext = this;
        _appliedTheme = AppServices.Settings.Current.Theme;
        _lastAdBlockEnabled = Settings.AdBlockEnabled;
        _lastDarkReaderEnabled = Settings.DarkReaderEnabled;
        _lastMicaEnabled = Settings.MicaEnabled;
        _lastGlassEnabled = Settings.GlassEnabled;
        _lastSurfaceOpacity = Settings.SurfaceOpacity;

        Width = Math.Max(MinWidth, Math.Min(1440, SystemParameters.WorkArea.Width - 72));
        Height = Math.Max(MinHeight, Math.Min(900, SystemParameters.WorkArea.Height - 72));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        if (incognito)
        {
            Title = "无痕模式 - XR Browser";
            ProfileInitial.Text = "隐";
            ProfileInitial.FontSize = 9;
            ProfileButton.ToolTip = "无痕模式";
        }

        _suggestionTimer.Tick += (_, _) => ShowSuggestions();
        _addressBlurTimer.Tick += (_, _) => CompleteAddressBlur();
        _findTimer.Tick += (_, _) => { _findTimer.Stop(); _ = RunFindAsync(true, true); };
        _toastTimer.Tick += (_, _) => HideToast();
        _sessionTimer.Tick += (_, _) => { _sessionTimer.Stop(); SaveSessionNow(); };

        Tabs.CollectionChanged += (_, _) =>
        {
            UpdateTabWidth();
            OnPropertyChanged(nameof(HasTabs));
        };

        AppServices.Bookmarks.Changed += Bookmarks_Changed;
        AppServices.Downloads.Changed += Downloads_Changed;
        AppServices.Downloads.DownloadCompleted += Downloads_Completed;
        AppServices.Settings.Changed += Settings_Changed;
        ThemeService.ThemeChanged += Theme_Changed;

        LoadSessionPlacement();
        ApplyStaticSettings();
    }

    public BrowserTab? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (ReferenceEquals(_activeTab, value)) return;
            _activeTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveTabTitle));
        }
    }

    public double TabWidth
    {
        get => (double)GetValue(TabWidthProperty);
        private set => SetValue(TabWidthProperty, value);
    }

    public bool HasTabs => Tabs.Count > 0;
    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool HasDownloads => Downloads.Count > 0;
    public int ActiveDownloadCount => Downloads.Count(d => d.IsActive);
    public bool IsIncognitoWindow => _isIncognito;
    public string OmniboxPlaceholder => $"在 {UrlHelper.GetSearchEngineName(AppServices.Settings.Current)} 中搜索，或输入网址";
    public Visibility ShowHomeButtonVisibility =>
        AppServices.Settings.Current.ShowHomeButton ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BookmarksBarVisibility =>
        AppServices.Settings.Current.ShowBookmarksBar ? Visibility.Visible : Visibility.Collapsed;
    public string ActiveTabTitle => ActiveTab?.Title ?? "";

    private AppSettings Settings => AppServices.Settings.Current;

    private void ApplyStaticSettings()
    {
        HomeButton.Visibility = ShowHomeButtonVisibility;
        BookmarksBar.Visibility = BookmarksBarVisibility;
        OnPropertyChanged(nameof(OmniboxPlaceholder));
        OnPropertyChanged(nameof(HasBookmarks));
        OnPropertyChanged(nameof(HasDownloads));
        OnPropertyChanged(nameof(ActiveDownloadCount));
    }

    private void LoadSessionPlacement()
    {
        if (!_isPrimary || _isIncognito || _pendingInitialUrl is not null) return;
        if (!Settings.RestoreSession || Settings.StartupMode != "continue") return;

        try
        {
            var session = AppServices.Sessions.Load();
            if (session.Tabs.Count == 0 && session.Width <= 0) return;
            _sessionToRestore = session;
            _restoreMaximized = session.Maximized;

            var maxWidth = Math.Max(MinWidth, SystemParameters.VirtualScreenWidth - 40);
            var maxHeight = Math.Max(MinHeight, SystemParameters.VirtualScreenHeight - 40);
            if (session.Width >= MinWidth && session.Height >= MinHeight)
            {
                Width = Math.Min(session.Width, maxWidth);
                Height = Math.Min(session.Height, maxHeight);
            }

            if (!double.IsNaN(session.Left) && !double.IsNaN(session.Top))
            {
                var left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(session.Left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 240));
                var top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(session.Top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 160));
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("Session placement failed: " + ex.Message);
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        NativeMethods.ApplyDarkFrame(this, ThemeService.IsDark);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        AppPaths.Log("Window_Loaded start");
        ApplyMica();
        try
        {
            _environment = await BrowserEnvironment.GetAsync(Settings);
            AppPaths.Log("Environment ready, initializing tabs");
            await InitializeTabsAsync();
            AppPaths.Log("Tabs initialized: " + Tabs.Count);
            ApplyBrowserTheme();
            _ = LoadBookmarkFaviconsAsync();
            _tabsReady = true;
            if (_restoreMaximized) WindowState = WindowState.Maximized;
            UpdateTabWidth();
            UpdateNavigationState();
            if (ActiveTab is not null) SelectTab(ActiveTab);
            StartupOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Startup failed: " + ex);
            StartupOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show(this,
                "浏览器引擎启动失败。\n\n" + ex.Message +
                "\n\n请确认已安装 Microsoft Edge WebView2 Runtime。",
                "XR Browser", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeTabsAsync()
    {
        if (!string.IsNullOrWhiteSpace(_pendingInitialUrl))
        {
            await CreateTabAsync(_pendingInitialUrl, activate: true);
            return;
        }

        if (_sessionToRestore is { Tabs.Count: > 0 } session)
        {
            var activeIndex = Math.Clamp(session.ActiveIndex, 0, session.Tabs.Count - 1);
            for (var i = 0; i < session.Tabs.Count; i++)
            {
                var entry = session.Tabs[i];
                var url = string.IsNullOrWhiteSpace(entry.Url) ? UrlHelper.NewTabUrl : entry.Url;
                await CreateTabAsync(url, activate: false);
            }
            if (Tabs.Count > 0) SelectTab(Tabs[Math.Clamp(activeIndex, 0, Tabs.Count - 1)]);
            _sessionToRestore = null;
            return;
        }

        var startUrl = Settings.StartupMode == "home" ? Settings.HomePage : "xr://newtab";
        await CreateTabAsync(startUrl, activate: true);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        AppPaths.Log("Window_Closing. Tabs=" + Tabs.Count + " " + Environment.StackTrace);
        _closing = true;
        _sessionTimer.Stop();
        if (_isPrimary && !_isIncognito) SaveSessionNow();
        try { AppServices.Settings.Save(); } catch { }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        AppPaths.Log("Window_Closed");
        _tabsReady = false;
        AppServices.Bookmarks.Changed -= Bookmarks_Changed;
        AppServices.Downloads.Changed -= Downloads_Changed;
        AppServices.Downloads.DownloadCompleted -= Downloads_Completed;
        AppServices.Settings.Changed -= Settings_Changed;
        ThemeService.ThemeChanged -= Theme_Changed;

        _suggestionTimer.Stop();
        _addressBlurTimer.Stop();
        _findTimer.Stop();
        _toastTimer.Stop();
        _sessionTimer.Stop();
        StatusPopup.IsOpen = false;
        ToastPopup.IsOpen = false;

        foreach (var tab in Tabs.ToList())
        {
            tab.PropertyChanged -= Tab_PropertyChanged;
            tab.FaviconUpdated -= Tab_FaviconUpdated;
            tab.FullScreenChanged -= Tab_FullScreenChanged;
            tab.Dispose();
        }
        Tabs.Clear();
        TabsHost.Children.Clear();
        AppServices.Sessions.Flush();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeGlyph is null) return;
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
        WindowFrame.BorderThickness = WindowState == WindowState.Maximized
            ? new Thickness(0)
            : new Thickness(1);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.T when shift:
                    ReopenClosedTab();
                    e.Handled = true;
                    return;
                case Key.T:
                    _ = CreateTabAsync("xr://newtab", activate: true);
                    e.Handled = true;
                    return;
                case Key.N when shift:
                    OpenNewWindow(null, incognito: true);
                    e.Handled = true;
                    return;
                case Key.N:
                    OpenNewWindow();
                    e.Handled = true;
                    return;
                case Key.W:
                    if (ActiveTab is not null) CloseTab(ActiveTab);
                    e.Handled = true;
                    return;
                case Key.L:
                case Key.E:
                    FocusAddressBar();
                    e.Handled = true;
                    return;
                case Key.D:
                    ToggleBookmark();
                    e.Handled = true;
                    return;
                case Key.H:
                    _ = OpenInternalPageAsync("history");
                    e.Handled = true;
                    return;
                case Key.J:
                    _ = OpenInternalPageAsync("downloads");
                    e.Handled = true;
                    return;
                case Key.F:
                    OpenFind();
                    e.Handled = true;
                    return;
                case Key.P:
                    PrintCurrentPage();
                    e.Handled = true;
                    return;
                case Key.R:
                    ActiveTab?.Reload();
                    e.Handled = true;
                    return;
                case Key.B when shift:
                    ToggleBookmarksBar();
                    e.Handled = true;
                    return;
                case Key.O when shift:
                    _ = OpenInternalPageAsync("bookmarks");
                    e.Handled = true;
                    return;
                case Key.I when shift:
                    OpenDevTools();
                    e.Handled = true;
                    return;
                case Key.Tab:
                    SelectRelativeTab(shift ? -1 : 1);
                    e.Handled = true;
                    return;
                case Key.PageDown:
                    SelectRelativeTab(1);
                    e.Handled = true;
                    return;
                case Key.PageUp:
                    SelectRelativeTab(-1);
                    e.Handled = true;
                    return;
                case Key.OemPlus:
                case Key.Add:
                    ZoomIn();
                    e.Handled = true;
                    return;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomOut();
                    e.Handled = true;
                    return;
                case Key.D0:
                case Key.NumPad0:
                    ResetZoom();
                    e.Handled = true;
                    return;
                case Key.D1:
                case Key.D2:
                case Key.D3:
                case Key.D4:
                case Key.D5:
                case Key.D6:
                case Key.D7:
                case Key.D8:
                    SelectTabByNumber((int)e.Key - (int)Key.D1);
                    e.Handled = true;
                    return;
                case Key.D9:
                    if (Tabs.Count > 0) SelectTab(Tabs[^1]);
                    e.Handled = true;
                    return;
            }
        }

        if (alt)
        {
            switch (e.Key)
            {
                case Key.Left:
                    ActiveTab?.GoBack();
                    e.Handled = true;
                    return;
                case Key.Right:
                    ActiveTab?.GoForward();
                    e.Handled = true;
                    return;
                case Key.Home:
                    NavigateHome();
                    e.Handled = true;
                    return;
                case Key.D:
                    FocusAddressBar();
                    e.Handled = true;
                    return;
                case Key.F:
                    MenuButton_Click(MenuButton, new RoutedEventArgs());
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.F5:
                ActiveTab?.Reload();
                e.Handled = true;
                break;
            case Key.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.F12:
                OpenDevTools();
                e.Handled = true;
                break;
            case Key.Escape:
                HandleEscape();
                e.Handled = true;
                break;
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => CloseToolbarPopups();

    private void Window_Deactivated(object? sender, EventArgs e) => CloseToolbarPopups();

    private void CloseToolbarPopups()
    {
        if (MainMenuPopup.IsOpen) ClosePopupAnimated(MainMenuPopup);
        if (ProfilePopup.IsOpen) ClosePopupAnimated(ProfilePopup);
        if (DownloadsPopup.IsOpen) ClosePopupAnimated(DownloadsPopup);
        if (ExtensionsPopup.IsOpen) ClosePopupAnimated(ExtensionsPopup);
        if (SiteInfoPopup.IsOpen) ClosePopupAnimated(SiteInfoPopup);
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (ActiveTab is null) return;
        if (e.Delta > 0) ZoomIn();
        else ZoomOut();
        e.Handled = true;
    }

    private void HandleEscape()
    {
        if (OmniboxPopup.IsOpen) { OmniboxPopup.IsOpen = false; return; }
        if (MainMenuPopup.IsOpen) { ClosePopupAnimated(MainMenuPopup); return; }
        if (ProfilePopup.IsOpen) { ClosePopupAnimated(ProfilePopup); return; }
        if (DownloadsPopup.IsOpen) { ClosePopupAnimated(DownloadsPopup); return; }
        if (ExtensionsPopup.IsOpen) { ClosePopupAnimated(ExtensionsPopup); return; }
        if (FindBar.Visibility == Visibility.Visible) { CloseFind(); return; }
        if (_fullscreen) { ExitFullScreen(); return; }
        if (ActiveTab?.IsLoading == true) ActiveTab.Stop();
    }

    private void FocusAddressBar()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void SelectRelativeTab(int delta)
    {
        if (Tabs.Count == 0) return;
        var index = ActiveTab is null ? 0 : Tabs.IndexOf(ActiveTab);
        index = (index + delta + Tabs.Count) % Tabs.Count;
        SelectTab(Tabs[index]);
    }

    private void SelectTabByNumber(int zeroBased)
    {
        if (zeroBased < Tabs.Count) SelectTab(Tabs[zeroBased]);
    }

    // ----- Tabs -----

    private void ConfigurePopupAnimations()
    {
        foreach (var popup in new[]
                 {
                     OmniboxPopup, MainMenuPopup, ProfilePopup, DownloadsPopup,
                     ExtensionsPopup, SiteInfoPopup, StatusPopup, ToastPopup
                 })
        {
            popup.PopupAnimation = PopupAnimation.None;
            popup.Opened += AnimatedPopup_Opened;
        }

        // These are toolbar menus rather than transient hover popups. Keeping
        // them open lets us animate the dismissal; outside clicks are handled
        // by Window_PreviewMouseDown / WebView focus.
        MainMenuPopup.StaysOpen = true;
        ProfilePopup.StaysOpen = true;
        DownloadsPopup.StaysOpen = true;
        ExtensionsPopup.StaysOpen = true;
        SiteInfoPopup.StaysOpen = true;
    }

    private async void AnimatedPopup_Opened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup || popup.Child is not FrameworkElement content) return;
        ApplyPopupMaterial(popup, content);
        if (content is Border clippedBorder)
        {
            clippedBorder.SizeChanged -= PopupBorder_SizeChanged;
            clippedBorder.SizeChanged += PopupBorder_SizeChanged;
            ApplyRoundedClip(clippedBorder);
        }
        if (content is Border popupBorder && popupBorder.Background is not DrawingBrush &&
            popup.Name is not (nameof(StatusPopup) or nameof(ToastPopup)))
            await PreparePopupBackdropAsync(content);
        if (!popup.IsOpen) return;
        StopSpring(content);

        if (!Settings.AnimationsEnabled)
        {
            ResetMotion(content);
            return;
        }

        content.RenderTransformOrigin = GetPopupOrigin(popup);
        var scale = new ScaleTransform(0.97, 0.97);
        var translate = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        content.RenderTransform = group;

        var offset = popup.Name is nameof(StatusPopup) or nameof(ToastPopup) ? 0 : 6;
        var blurTarget = content is Border { Child: FrameworkElement inner } ? inner : content;

        var spring = new SpringAnimation((progress, velocity) =>
        {
            content.Opacity = Math.Clamp(progress, 0, 1);
            var value = 0.97 + 0.03 * progress;
            value = Math.Clamp(value, 0.97, 1.012);
            scale.ScaleX = value;
            scale.ScaleY = value;
            translate.Y = offset * (1 - progress);
            ApplyMotionBlur(blurTarget, velocity, 2.8, 0.28);
        }, () =>
        {
            content.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            translate.Y = 0;
            ApplyMotionBlur(blurTarget, 0, 2.8, 0.28);
            _springs.Remove(content);
        }, stiffness: 280, damping: 26, speed: Settings.AnimationSpeed);

        _springs[content] = spring;
        spring.Start(0, 1);
    }

    private void TabRoot_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement root) return;
        root.SizeChanged -= TabRoot_SizeChanged;
        root.SizeChanged += TabRoot_SizeChanged;
        UpdateTabIndicator(false);
        if (!Settings.AnimationsEnabled) return;

        StopSpring(root);
        var scale = new ScaleTransform(0.985, 0.985);
        var translate = new TranslateTransform(0, 4);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        root.RenderTransform = group;

        var spring = new SpringAnimation((progress, velocity) =>
        {
            root.Opacity = Math.Clamp(progress, 0, 1);
            var value = 0.985 + 0.015 * progress;
            value = Math.Clamp(value, 0.985, 1.006);
            scale.ScaleX = value;
            scale.ScaleY = value;
            translate.Y = 4 * (1 - progress);
            ApplyMotionBlur(root, velocity, 1.8, 0.22);
        }, () =>
        {
            root.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            translate.Y = 0;
            ApplyMotionBlur(root, 0, 1.8, 0.22);
            _springs.Remove(root);
        }, stiffness: 270, damping: 25, speed: Settings.AnimationSpeed);

        _springs[root] = spring;
        spring.Start(0, 1);
    }

    private void AnimateOrCloseTab(BrowserTab tab)
    {
        if (!Settings.AnimationsEnabled)
        {
            CloseTab(tab);
            return;
        }
        if (!_closingTabs.Add(tab)) return;

        var root = FindTabRoot(tab);
        if (root is null)
        {
            _closingTabs.Remove(tab);
            CloseTab(tab);
            return;
        }

        var start = 1.0;
        var initialVelocity = 0.0;
        if (_springs.Remove(root, out var previous))
        {
            start = Math.Clamp(previous.Value, 0, 1);
            initialVelocity = previous.Velocity;
            previous.Stop();
        }

        root.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        root.RenderTransform = scale;

        var spring = new SpringAnimation((progress, velocity) =>
        {
            root.Opacity = Math.Clamp(progress, 0, 1);
            var value = 0.94 + 0.06 * Math.Clamp(progress, 0, 1);
            scale.ScaleX = value;
            scale.ScaleY = value;
            ApplyMotionBlur(root, velocity, 1.9, 0.22);
        }, () =>
        {
            root.Opacity = 0;
            ApplyMotionBlur(root, 0, 1.9, 0.22);
            _closingTabs.Remove(tab);
            CloseTab(tab);
        }, stiffness: 300, damping: 30, speed: Settings.AnimationSpeed);

        _springs[root] = spring;
        spring.Start(start, 0, initialVelocity);
    }

    private void ClosePopupAnimated(Popup popup)
    {
        if (!popup.IsOpen || !_closingPopups.Add(popup)) return;

        if (!Settings.AnimationsEnabled || popup.Child is not FrameworkElement content)
        {
            _closingPopups.Remove(popup);
            popup.IsOpen = false;
            return;
        }

        StopSpring(content);
        content.RenderTransformOrigin = GetPopupOrigin(popup);
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        content.RenderTransform = group;

        var blurTarget = content is Border { Child: FrameworkElement inner } ? inner : content;
        var spring = new SpringAnimation((progress, velocity) =>
        {
            var value = Math.Clamp(progress, 0, 1);
            content.Opacity = value;
            var scaleValue = 0.88 + 0.12 * value;
            scale.ScaleX = scaleValue;
            scale.ScaleY = scaleValue;
            var towardButton = popup.Name is nameof(MainMenuPopup) or nameof(ProfilePopup)
                or nameof(DownloadsPopup) or nameof(ExtensionsPopup);
            translate.Y = (towardButton ? -7 : 6) * (1 - value);
            ApplyMotionBlur(blurTarget, velocity, 1.8, 0.22);
        }, () =>
        {
            popup.IsOpen = false;
            content.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            translate.Y = 0;
            ApplyMotionBlur(blurTarget, 0, 1.8, 0.22);
            _springs.Remove(content);
            _closingPopups.Remove(popup);
        }, stiffness: 280, damping: 30, speed: Settings.AnimationSpeed);

        _springs[content] = spring;
        spring.Start(1, 0);
    }

    private static Point GetPopupOrigin(Popup popup)
        => popup.Name switch
        {
            nameof(MainMenuPopup) or nameof(ProfilePopup) or nameof(DownloadsPopup) or nameof(ExtensionsPopup)
                => new Point(1, 0),
            nameof(SiteInfoPopup) => new Point(0, 0),
            nameof(OmniboxPopup) => new Point(0.5, 0),
            _ => new Point(0.5, 0.5)
        };

    private async Task PreparePopupBackdropAsync(FrameworkElement content)
    {
        content.Opacity = 0;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Render);
            await Task.Delay(16);
            if (content.ActualWidth < 2 || content.ActualHeight < 2) return;
            var dpi = VisualTreeHelper.GetDpi(content);
            var point = content.PointToScreen(new Point(0, 0));
            var brush = CreateBackdropBrush(
                (int)Math.Round(point.X), (int)Math.Round(point.Y),
                new Size(content.ActualWidth, content.ActualHeight), dpi.DpiScaleX);
            if (brush is not null && content is Border border)
                border.Background = brush;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Popup backdrop failed: " + ex.Message);
        }
        finally
        {
            content.Opacity = 1;
        }
    }

    private void PopupBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border) ApplyRoundedClip(border);
    }

    private static void ApplyRoundedClip(Border border)
    {
        if (border.ActualWidth < 2 || border.ActualHeight < 2) return;
        border.Clip = new RectangleGeometry(
            new Rect(0, 0, border.ActualWidth, border.ActualHeight), 11, 11);
    }

    private void PreparePopupBackdrop(Popup popup)
    {
        if (popup.Child is not Border border || popup.PlacementTarget is not FrameworkElement target)
            return;

        try
        {
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = border.DesiredSize;
            if (size.Width < 2 || size.Height < 2) return;
            var point = target.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(border).DpiScaleX;
            var x = (int)Math.Round(point.X + popup.HorizontalOffset * dpi);
            var y = (int)Math.Round(point.Y + target.RenderSize.Height * dpi + popup.VerticalOffset * dpi);
            var brush = CreateBackdropBrush(x, y, size, dpi);
            if (brush is not null) border.Background = brush;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Popup backdrop prepare failed: " + ex.Message);
        }
    }

    private void ApplyPopupMaterial(Popup popup, FrameworkElement content)
    {
        if (popup.Name is nameof(StatusPopup) or nameof(ToastPopup)) return;

        var opacity = Math.Clamp(Settings.SurfaceOpacity + 0.10, 0.5, 0.94);
        if (content is Border border && border.Background is not DrawingBrush &&
            FindResource("MenuBackgroundBrush") is SolidColorBrush baseBrush)
        {
            var color = baseBrush.Color;
            border.Background = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(255 * opacity), color.R, color.G, color.B));
        }

    }

    private void StopSpring(FrameworkElement element)
    {
        if (!_springs.Remove(element, out var spring)) return;
        spring.Stop();
    }

    private void ResetMotion(FrameworkElement element)
    {
        StopSpring(element);
        element.Opacity = 1;
        element.RenderTransform = Transform.Identity;
        ApplyMotionBlur(element, 0, 0, 0);
        if (element is Border { Child: FrameworkElement child })
            ApplyMotionBlur(child, 0, 0, 0);
    }

    private static void ApplyMotionBlur(FrameworkElement target, double velocity, double maxRadius, double factor)
    {
        if (maxRadius <= 0 || factor <= 0)
        {
            if (target.Effect is BlurEffect) target.Effect = null;
            return;
        }

        var radius = Math.Min(maxRadius, Math.Abs(velocity) * factor);
        if (radius < 0.12)
        {
            if (target.Effect is BlurEffect) target.Effect = null;
            return;
        }

        if (target.Effect is null)
            target.Effect = new BlurEffect
            {
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            };
        if (target.Effect is BlurEffect blur)
            blur.Radius = radius;
    }

    private FrameworkElement? FindTabRoot(BrowserTab tab)
    {
        if (TabsItems.ItemContainerGenerator.ContainerFromItem(tab) is not DependencyObject container)
            return null;
        return FindVisualChild<FrameworkElement>(container, "TabRoot");
    }

    private void TabRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_tabIndicatorPending) return;
        if (sender is FrameworkElement root &&
            root.DataContext is BrowserTab tab &&
            ReferenceEquals(tab, ActiveTab) &&
            _tabIndicatorSpring is null)
        {
            UpdateTabIndicator(false);
        }
    }

    private void UpdateTabIndicator(bool animate)
    {
        if (ActiveTab is null || TabsGrid is null)
        {
            ActiveTabIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var root = FindTabRoot(ActiveTab);
        if (root is null || root.ActualWidth < 2 || TabsGrid.ActualWidth < 2)
        {
            ActiveTabIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var origin = root.TransformToAncestor(TabsGrid).Transform(new Point(0, 0));
        var indicatorWidth = root.ActualWidth;
        var targetX = origin.X;
        ActiveTabIndicator.Width = indicatorWidth;
        ActiveTabIndicator.Visibility = Visibility.Visible;

        if (!_tabIndicatorReady || !animate || !Settings.AnimationsEnabled)
        {
            _tabIndicatorSpring?.Stop();
            _tabIndicatorSpring = null;
            ActiveTabIndicatorTranslate.X = targetX;
            ActiveTabIndicatorScale.ScaleX = 1;
            ActiveTabIndicatorScale.ScaleY = 1;
            _tabIndicatorReady = true;
            return;
        }

        var startX = ActiveTabIndicatorTranslate.X;
        _tabIndicatorSpring?.Stop();
        var spring = new SpringAnimation((progress, velocity) =>
        {
            var value = Math.Clamp(progress, 0, 1.03);
            ActiveTabIndicatorTranslate.X = startX + (targetX - startX) * value;

            // A little squash-and-stretch in the direction of motion gives the
            // indicator the springy iOS bottom-bar feel.
            var stretch = 1 + Math.Min(Math.Abs(velocity) * 0.012, 0.08);
            ActiveTabIndicatorScale.ScaleX = stretch;
            ActiveTabIndicatorScale.ScaleY = 1 - (stretch - 1) * 0.4;
        }, () =>
        {
            ActiveTabIndicatorTranslate.X = targetX;
            ActiveTabIndicatorScale.ScaleX = 1;
            ActiveTabIndicatorScale.ScaleY = 1;
            _tabIndicatorSpring = null;
        }, stiffness: 260, damping: 22, speed: Settings.AnimationSpeed);

        _tabIndicatorSpring = spring;
        spring.Start(0, 1);
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name) return element;
            var nested = FindVisualChild<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void SelectTab(BrowserTab tab)
    {
        if (!Tabs.Contains(tab)) return;
        if (ReferenceEquals(tab, ActiveTab))
        {
            tab.SetActive(true);
            UpdateCrashOverlay();
            UpdateTabIndicator(false);
            return;
        }

        ActiveTab?.SetActive(false);
        _tabIndicatorPending = true;
        ActiveTab = tab;
        tab.SetActive(true);
        UpdateAddressBar(force: true);
        UpdateNavigationState();
        UpdateBookmarkState();
        UpdateSecurityIcon();
        UpdateWindowTitle();
        UpdateCrashOverlay();
        if (FindBar.Visibility == Visibility.Visible) CloseFind();
        ScrollTabIntoView(tab);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateTabIndicator(true);
            _tabIndicatorPending = false;
        }), DispatcherPriority.Loaded);
        try { tab.WebView.Focus(); } catch { }
        ScheduleSessionSave();
    }

    private void ScrollTabIntoView(BrowserTab tab)
    {
        try
        {
            if (TabsItems.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
                container.BringIntoView();
        }
        catch { }
    }

    public void CloseTab(BrowserTab tab, bool record = true)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        var tabRoot = FindTabRoot(tab);
        if (tabRoot is not null) StopSpring(tabRoot);

        if (record)
        {
            _closedTabs.Insert(0, new ClosedTab
            {
                Url = UrlHelper.ToCanonicalUrl(tab.Url),
                Title = tab.Title,
                Index = index
            });
            if (_closedTabs.Count > 25) _closedTabs.RemoveRange(25, _closedTabs.Count - 25);
        }

        var wasActive = ReferenceEquals(tab, ActiveTab);
        Tabs.Remove(tab);
        if (TabsHost.Children.Contains(tab.WebView)) TabsHost.Children.Remove(tab.WebView);
        tab.PropertyChanged -= Tab_PropertyChanged;
        tab.FaviconUpdated -= Tab_FaviconUpdated;
        tab.FullScreenChanged -= Tab_FullScreenChanged;
        tab.Dispose();

        if (Tabs.Count == 0)
        {
            if (!_closing) Close();
            return;
        }

        if (wasActive)
        {
            var nextIndex = Math.Min(index, Tabs.Count - 1);
            ActiveTab = null;
            SelectTab(Tabs[nextIndex]);
        }
        UpdateTabWidth();
        ScheduleSessionSave();
    }

    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            ShowToast("没有最近关闭的标签页");
            return;
        }
        var closed = _closedTabs[0];
        _closedTabs.RemoveAt(0);
        _ = CreateTabAsync(closed.Url, activate: true, index: Math.Clamp(closed.Index, 0, Tabs.Count));
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not BrowserTab tab) return;
        if (e.ClickCount == 1)
        {
            SelectTab(tab);
            _tabDragTab = tab;
            _tabDragStart = e.GetPosition(TabsScrollViewer);
            _tabDragArmed = true;
        }
        e.Handled = true;
    }

    private void Tab_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_tabDragArmed || e.LeftButton != MouseButtonState.Pressed || _tabDragTab is null) return;
        var point = e.GetPosition(TabsScrollViewer);
        if (Math.Abs(point.X - _tabDragStart.X) < 7 && Math.Abs(point.Y - _tabDragStart.Y) < 7) return;

        var dragged = _tabDragTab;
        var dragCanceled = false;
        QueryContinueDragEventHandler cancelHandler = (_, args) =>
        {
            if (!args.EscapePressed) return;
            dragCanceled = true;
            args.Action = DragAction.Cancel;
        };

        _tabDragArmed = false;
        _tabDragging = true;
        TabsItems.QueryContinueDrag += cancelHandler;
        try
        {
            var result = DragDrop.DoDragDrop(TabsItems, dragged, DragDropEffects.Move);
            if (result == DragDropEffects.None && !dragCanceled)
                TearOffTab(dragged);
        }
        catch { }
        finally
        {
            TabsItems.QueryContinueDrag -= cancelHandler;
            _tabDragging = false;
            _tabDragTab = null;
        }
    }

    private void TearOffTab(BrowserTab tab)
    {
        if (!Tabs.Contains(tab) || _closing) return;

        var url = string.IsNullOrWhiteSpace(tab.Url) ? UrlHelper.NewTabUrl : tab.Url;
        var incognito = tab.IsIncognito;
        var position = NativeMethods.GetCursorPos(out var cursor)
            ? new Point(cursor.X, cursor.Y)
            : new Point(Left + 140, Top + 24);
        OpenNewWindow(url, incognito, position);
        CloseTab(tab);
    }

    private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_tabDragging) return;
        _tabDragArmed = false;
        _tabDragTab = null;
    }

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        if (sender is FrameworkElement fe && fe.DataContext is BrowserTab tab)
        {
            AnimateOrCloseTab(tab);
            e.Handled = true;
        }
    }

    private void Tabs_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(BrowserTab)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Tabs_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BrowserTab)) is not BrowserTab dragged) return;
        var oldIndex = Tabs.IndexOf(dragged);
        if (oldIndex < 0) return;

        var newIndex = Tabs.Count - 1;
        var hit = TabsItems.InputHitTest(e.GetPosition(TabsItems)) as DependencyObject;
        if (hit is not null && ItemsControl.ContainerFromElement(TabsItems, hit) is FrameworkElement container &&
            container.DataContext is BrowserTab target)
        {
            newIndex = Tabs.IndexOf(target);
        }

        if (newIndex < 0) newIndex = Tabs.Count - 1;
        if (newIndex == oldIndex) return;

        Tabs.Move(oldIndex, newIndex);
        if (TabsHost.Children.Contains(dragged.WebView))
        {
            TabsHost.Children.Remove(dragged.WebView);
            TabsHost.Children.Insert(newIndex, dragged.WebView);
        }
        SelectTab(dragged);
        e.Handled = true;
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BrowserTab tab)
        {
            AnimateOrCloseTab(tab);
            e.Handled = true;
        }
    }

    private void Tab_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.ContextMenu is not ContextMenu menu) return;
        if (menu.Tag is not true)
        {
            PrepareAnimatedContextMenu(menu);
            menu.Tag = true;
        }
        menu.DataContext = fe.DataContext;
        if (fe.DataContext is BrowserTab tab)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (Equals(item.Tag, "Mute"))
                    item.Header = tab.IsMuted ? "取消静音" : "静音标签页";
            }
        }
    }

    private static BrowserTab? TabFromMenuItem(object sender)
    {
        if (sender is MenuItem mi)
        {
            if (mi.DataContext is BrowserTab tab) return tab;
            if (mi.Parent is ContextMenu cm)
            {
                if (cm.DataContext is BrowserTab cmTab) return cmTab;
                if (cm.PlacementTarget is FrameworkElement fe && fe.DataContext is BrowserTab placementTab)
                    return placementTab;
            }
        }
        return null;
    }

    private void TabMenu_Reload(object sender, RoutedEventArgs e) => TabFromMenuItem(sender)?.Reload();
    private void TabMenu_Mute(object sender, RoutedEventArgs e) => TabFromMenuItem(sender)?.ToggleMute();

    private void TabMenu_Duplicate(object sender, RoutedEventArgs e)
    {
        var tab = TabFromMenuItem(sender);
        if (tab is not null) _ = CreateTabAsync(UrlHelper.ToCanonicalUrl(tab.Url), activate: true);
    }

    private void TabMenu_Close(object sender, RoutedEventArgs e)
    {
        var tab = TabFromMenuItem(sender);
        if (tab is not null) CloseTab(tab);
    }

    private void TabMenu_CloseOthers(object sender, RoutedEventArgs e)
    {
        var tab = TabFromMenuItem(sender);
        if (tab is null) return;
        foreach (var other in Tabs.Where(t => !ReferenceEquals(t, tab)).ToList()) CloseTab(other);
        SelectTab(tab);
    }

    private void TabMenu_CloseRight(object sender, RoutedEventArgs e)
    {
        var tab = TabFromMenuItem(sender);
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        foreach (var other in Tabs.Skip(index + 1).ToList()) CloseTab(other);
        SelectTab(tab);
    }

    private void TabsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTabWidth(animate: false);

    private void UpdateTabWidth(bool animate = true)
    {
        var available = TabsScrollViewer.ActualWidth;
        if (available <= 0 || Tabs.Count == 0) return;
        var width = Math.Clamp((available - 96.0) / Tabs.Count, 108, 240);
        if (Math.Abs(TabWidth - width) < 0.5) return;

        if (!animate || !Settings.AnimationsEnabled)
        {
            BeginAnimation(TabWidthProperty, null);
            TabWidth = width;
            return;
        }

        BeginAnimation(TabWidthProperty,
            new DoubleAnimation(TabWidth, width, TimeSpan.FromMilliseconds(300 / Math.Clamp(Settings.AnimationSpeed, 0.5, 2.0)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BrowserTab tab) return;
        if (ReferenceEquals(tab, ActiveTab))
        {
            switch (e.PropertyName)
            {
                case nameof(BrowserTab.Url):
                case nameof(BrowserTab.DisplayUrl):
                    UpdateAddressBar();
                    UpdateBookmarkState();
                    UpdateSecurityIcon();
                    UpdateWindowTitle();
                    break;
                case nameof(BrowserTab.Title):
                    UpdateWindowTitle();
                    break;
                case nameof(BrowserTab.IsLoading):
                    UpdateNavigationState();
                    break;
                case nameof(BrowserTab.IsCrashed):
                    UpdateCrashOverlay();
                    break;
                case nameof(BrowserTab.IsBookmarked):
                    UpdateBookmarkState();
                    break;
            }
        }

        if (e.PropertyName is nameof(BrowserTab.Url) or nameof(BrowserTab.Title))
            ScheduleSessionSave();
    }

    private void Tab_FaviconUpdated(object? sender, EventArgs e)
    {
        if (sender is BrowserTab tab) _ = LoadFaviconAsync(tab);
    }

    private async Task LoadFaviconAsync(BrowserTab tab)
    {
        var url = tab.Url;
        var image = await AppServices.Favicons.GetAsync(tab.Core, url);
        if (image is null || tab.Url != url || tab.IsDisposed) return;

        tab.Favicon = image;
        foreach (var bookmark in Bookmarks)
        {
            if (string.Equals(bookmark.Url, url, StringComparison.OrdinalIgnoreCase))
                bookmark.Favicon = image;
        }
    }

    private async Task LoadBookmarkFaviconsAsync()
    {
        var pending = Bookmarks.Where(b => !b.HasFavicon).ToList();
        if (pending.Count == 0) return;

        await Task.WhenAll(pending.Select(async bookmark =>
        {
            var image = await AppServices.Favicons.GetForBookmarkAsync(bookmark.Url);
            if (image is not null && !bookmark.HasFavicon)
                bookmark.Favicon = image;
        }));
    }

    private async Task LoadBookmarkFaviconAsync(Bookmark bookmark)
    {
        if (bookmark.HasFavicon) return;
        var image = await AppServices.Favicons.GetForBookmarkAsync(bookmark.Url);
        if (image is not null) bookmark.Favicon = image;
    }

    private void Tab_FullScreenChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, ActiveTab)) return;
        var active = sender as BrowserTab;
        if (active?.Core?.ContainsFullScreenElement == true) EnterFullScreen();
        else if (_fullscreen) ExitFullScreen();
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        _ = CreateTabAsync("xr://newtab", activate: true);
    }

    private void TabChevron_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = TabChevronButton, Placement = PlacementMode.Bottom };
        var newWindow = new MenuItem { Header = "新建窗口" };
        newWindow.Click += (_, _) => OpenNewWindow();
        var incognito = new MenuItem { Header = "新建无痕窗口" };
        incognito.Click += (_, _) => OpenNewWindow(null, true);
        var newTab = new MenuItem { Header = "新建标签页" };
        newTab.Click += (_, _) => _ = CreateTabAsync("xr://newtab", activate: true);
        menu.Items.Add(newTab);
        menu.Items.Add(newWindow);
        menu.Items.Add(incognito);
        menu.IsOpen = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ActiveTab?.GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => ActiveTab?.GoForward();

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        if (ActiveTab.IsLoading) ActiveTab.Stop();
        else ActiveTab.Reload();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => NavigateHome();

    private void NavigateHome()
    {
        if (ActiveTab is null)
        {
            _ = CreateTabAsync(Settings.HomePage, activate: true);
            return;
        }
        NavigateTab(ActiveTab, Settings.HomePage);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { }
    }

    private void Titlebar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = WindowFrame, Placement = PlacementMode.MousePoint };
        var minimize = new MenuItem { Header = "最小化" };
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var maximize = new MenuItem { Header = WindowState == WindowState.Maximized ? "还原" : "最大化" };
        maximize.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        var close = new MenuItem { Header = "关闭" };
        close.Click += (_, _) => Close();
        menu.Items.Add(minimize);
        menu.Items.Add(maximize);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void UpdateNavigationState()
    {
        var loading = ActiveTab?.IsLoading == true;
        LoadingBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ReloadGlyph.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        StopGlyph.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ReloadButton.ToolTip = loading ? "停止加载 (Esc)" : "重新加载 (Ctrl+R)";
        StatusPopup.IsOpen = false;
    }

    private void UpdateCrashOverlay()
    {
        var crashed = ActiveTab?.IsCrashed == true;
        CrashOverlay.Visibility = crashed ? Visibility.Visible : Visibility.Collapsed;
        if (ActiveTab is null) return;
        if (crashed) ActiveTab.WebView.Visibility = Visibility.Collapsed;
        else if (ActiveTab.IsActive) ActiveTab.WebView.Visibility = Visibility.Visible;
    }

    private void UpdateWindowTitle()
    {
        var title = ActiveTab?.Title;
        if (string.IsNullOrWhiteSpace(title) || title == "无标题") title = ActiveTab?.IsNewTabPage == true ? "新标签页" : "XR Browser";
        var prefix = _isIncognito ? "[无痕] " : "";
        Title = $"{prefix}{title} - XR Browser";
        OnPropertyChanged(nameof(ActiveTabTitle));
    }

    // ----- Address bar -----

    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressAddress) return;
        if (!_addressFocused) return;
        _suggestionTimer.Stop();
        _suggestionTimer.Start();
    }

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _addressFocused = true;
        _addressBlurTimer.Stop();
        _suppressAddress = true;
        AddressBox.Text = UrlHelper.GetFullDisplayUrl(ActiveTab?.Url);
        _suppressAddress = false;
        AddressBox.SelectAll();
        ShowSuggestions();
    }

    private void AddressBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _addressBlurTimer.Stop();
        _addressBlurTimer.Start();
    }

    private void AddressBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressBox.IsKeyboardFocusWithin)
        {
            AddressBox.Focus();
            e.Handled = true;
        }
    }

    private void CompleteAddressBlur()
    {
        if (AddressBox.IsKeyboardFocusWithin) return;
        _addressFocused = false;
        UpdateAddressBar(force: true);
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!OmniboxPopup.IsOpen) ShowSuggestions();
                if (Suggestions.Count == 0) return;
                SuggestionList.SelectedIndex = Math.Min(SuggestionList.SelectedIndex + 1, Suggestions.Count - 1);
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (!OmniboxPopup.IsOpen) return;
                SuggestionList.SelectedIndex = Math.Max(SuggestionList.SelectedIndex - 1, 0);
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                if (SuggestionList.SelectedItem is OmniboxSuggestion selected)
                    NavigateToSuggestion(selected);
                else
                    NavigateFromAddress();
                e.Handled = true;
                break;
            case Key.Escape:
                OmniboxPopup.IsOpen = false;
                _suppressAddress = true;
                AddressBox.Text = UrlHelper.GetOmniboxText(ActiveTab?.Url, false);
                _suppressAddress = false;
                AddressBox.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void NavigateFromAddress()
    {
        var input = AddressBox.Text.Trim();
        OmniboxPopup.IsOpen = false;
        if (input.Length == 0)
        {
            NavigateTab(ActiveTab, "xr://newtab");
            return;
        }

        if (!UrlHelper.TryNormalizeNavigation(input, Settings, out var target, out _))
            return;

        NavigateTab(ActiveTab, target);
        _addressFocused = false;
        OmniboxPopup.IsOpen = false;
        try
        {
            Keyboard.ClearFocus();
            ActiveTab?.WebView.Focus();
        }
        catch { }
    }

    private void NavigateToSuggestion(OmniboxSuggestion suggestion)
    {
        OmniboxPopup.IsOpen = false;
        NavigateTab(ActiveTab, suggestion.Target);
    }

    private void ShowSuggestions()
    {
        if (!_addressFocused || ActiveTab is null) return;
        BuildSuggestions(AddressBox.Text.Trim());
        if (Suggestions.Count == 0)
        {
            OmniboxPopup.IsOpen = false;
            return;
        }

        try
        {
            OmniboxPopupBorder.Width = Math.Max(OmniboxBorder.ActualWidth, 320);
            SuggestionList.SelectedIndex = -1;
            OmniboxPopup.IsOpen = true;
        }
        catch (InvalidOperationException)
        {
            OmniboxPopup.IsOpen = false;
        }
    }

    private void BuildSuggestions(string query)
    {
        Suggestions.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(OmniboxSuggestion suggestion)
        {
            if (!seen.Add(suggestion.Target)) return;
            Suggestions.Add(suggestion);
        }

        if (query.Length == 0)
        {
            foreach (var entry in AppServices.History.TopSites(5))
            {
                Add(new OmniboxSuggestion
                {
                    Title = entry.Title,
                    Detail = entry.Host,
                    Target = entry.Url,
                    IconGlyph = "\uE81C"
                });
            }
            foreach (var bookmark in Bookmarks.Take(4))
            {
                Add(new OmniboxSuggestion
                {
                    Title = bookmark.Title,
                    Detail = bookmark.Url,
                    Target = bookmark.Url,
                    IconGlyph = "\uE8A4",
                    IsBookmark = true
                });
            }
            return;
        }

        Add(new OmniboxSuggestion
        {
            Title = query,
            Detail = $"使用 {UrlHelper.GetSearchEngineName(Settings)} 搜索",
            Target = UrlHelper.GetSearchUrl(query, Settings),
            IconGlyph = "\uE721",
            IsSearch = true
        });

        if (UrlHelper.TryNormalizeNavigation(query, Settings, out var directTarget, out var isSearch) && !isSearch &&
            (directTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             directTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            Add(new OmniboxSuggestion
            {
                Title = directTarget,
                Detail = "打开网址",
                Target = directTarget,
                IconGlyph = "\uE774"
            });
        }

        foreach (var entry in AppServices.History.Search(query, 6))
        {
            Add(new OmniboxSuggestion
            {
                Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title,
                Detail = entry.Host,
                Target = entry.Url,
                IconGlyph = "\uE81C"
            });
        }

        foreach (var bookmark in Bookmarks.Where(b =>
                     b.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     b.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(4))
        {
            Add(new OmniboxSuggestion
            {
                Title = bookmark.Title,
                Detail = bookmark.Url,
                Target = bookmark.Url,
                IconGlyph = "\uE8A4",
                IsBookmark = true
            });
        }

        while (Suggestions.Count > 10) Suggestions.RemoveAt(Suggestions.Count - 1);
    }

    private void SuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(SuggestionList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is OmniboxSuggestion suggestion)
        {
            NavigateToSuggestion(suggestion);
            e.Handled = true;
        }
    }

    private void SuggestionList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(SuggestionList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item is not null) SuggestionList.SelectedItem = item.DataContext;
    }

    private void UpdateAddressBar(bool force = false)
    {
        if (ActiveTab is null)
        {
            _suppressAddress = true;
            AddressBox.Text = "";
            _suppressAddress = false;
            return;
        }

        if (_addressFocused && !force) return;
        _suppressAddress = true;
        AddressBox.Text = UrlHelper.GetOmniboxText(ActiveTab.Url, false);
        _suppressAddress = false;
    }

    private void UpdateSecurityIcon()
    {
        var url = ActiveTab?.Url;
        var state = UrlHelper.GetSecurityState(url);
        (string glyph, string tooltip) = state switch
        {
            SecurityState.Secure => ("\uE72E", "连接安全"),
            SecurityState.Insecure => ("\uE946", "此网站不使用安全连接"),
            SecurityState.Local => ("\uE774", "本地页面"),
            SecurityState.Internal => ("\uE774", "XR Browser 内置页面"),
            _ => ("\uE774", "网站信息")
        };
        SecurityButton.Content = glyph;
        SecurityButton.ToolTip = tooltip;
        SecurityButton.Foreground = state == SecurityState.Insecure
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("IconBrush");

        SiteInfoGlyph.Text = glyph;
        SiteInfoTitle.Text = state switch
        {
            SecurityState.Secure => "连接安全",
            SecurityState.Insecure => "连接不安全",
            SecurityState.Local => "本地页面",
            SecurityState.Internal => "XR Browser 页面",
            _ => "网站信息"
        };
        SiteInfoHost.Text = string.IsNullOrWhiteSpace(url) ? "新标签页" : UrlHelper.GetHost(url);
        SiteInfoDescription.Text = state switch
        {
            SecurityState.Secure => "此网站使用 HTTPS 安全连接。你在此网站上输入的信息在传输过程中会被加密。",
            SecurityState.Insecure => "此网站未使用安全连接。请勿在此网站上输入密码、银行卡等敏感信息。",
            SecurityState.Local => "这是存储在本机的页面。",
            SecurityState.Internal => "这是 XR Browser 的内置页面，由本地资源提供。",
            _ => "没有可用的网站安全信息。"
        };
    }

    private void UpdateBookmarkState()
    {
        var url = GetBookmarkUrl(ActiveTab);
        var marked = url is not null && AppServices.Bookmarks.Contains(url);
        if (ActiveTab is not null) ActiveTab.IsBookmarked = marked;
        BookmarkStarButton.IsEnabled = url is not null;
        BookmarkStarButton.Content = marked ? "\uE735" : "\uE734";
        BookmarkStarButton.Foreground = marked
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("IconBrush");
        BookmarkStarButton.ToolTip = marked ? "修改或移除此书签" : "为此页面添加书签 (Ctrl+D)";
    }

    private static string? GetBookmarkUrl(BrowserTab? tab)
    {
        if (tab is null || string.IsNullOrWhiteSpace(tab.Url)) return null;
        if (UrlHelper.IsNewTabUrl(tab.Url)) return null;
        return UrlHelper.IsInternalUrl(tab.Url) ? UrlHelper.ToCanonicalUrl(tab.Url) : tab.Url;
    }

    private void ToggleBookmark()
    {
        var tab = ActiveTab;
        var url = GetBookmarkUrl(tab);
        if (tab is null || url is null) return;
        if (AppServices.Bookmarks.Contains(url))
        {
            AppServices.Bookmarks.Remove(url);
            ShowToast("已移除书签");
        }
        else
        {
            var bookmark = AppServices.Bookmarks.Add(tab.Title, url);
            if (tab.Favicon is not null)
                bookmark.Favicon = tab.Favicon;
            else
                _ = LoadBookmarkFaviconAsync(bookmark);
            ShowToast("已添加书签");
        }
        UpdateBookmarkState();
        _ = InternalPageBridge.NotifyAsync(this, "bookmarksChanged", null);
    }

    private void BookmarkStar_Click(object sender, RoutedEventArgs e) => ToggleBookmark();

    private void Bookmarks_Changed(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasBookmarks));
        UpdateBookmarkState();
        _ = LoadBookmarkFaviconsAsync();
    }

    // ----- Bookmarks bar / menus -----

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Bookmark bookmark)
            NavigateTab(ActiveTab, bookmark.Url);
    }

    private static Bookmark? BookmarkFromMenuItem(object sender)
    {
        if (sender is MenuItem mi)
        {
            if (mi.DataContext is Bookmark b) return b;
            if (mi.Parent is ContextMenu cm)
            {
                if (cm.DataContext is Bookmark cmB) return cmB;
                if (cm.PlacementTarget is FrameworkElement fe && fe.DataContext is Bookmark placementB)
                    return placementB;
            }
        }
        return null;
    }

    private void BookmarkMenu_Open(object sender, RoutedEventArgs e)
    {
        var b = BookmarkFromMenuItem(sender);
        if (b is not null) NavigateTab(ActiveTab, b.Url);
    }

    private void BookmarkMenu_OpenNewTab(object sender, RoutedEventArgs e)
    {
        var b = BookmarkFromMenuItem(sender);
        if (b is not null) _ = CreateTabAsync(b.Url, activate: true);
    }

    private void BookmarkMenu_OpenNewWindow(object sender, RoutedEventArgs e)
    {
        var b = BookmarkFromMenuItem(sender);
        if (b is not null) OpenNewWindow(b.Url);
    }

    private void BookmarkMenu_Edit(object sender, RoutedEventArgs e)
    {
        var bookmark = BookmarkFromMenuItem(sender);
        if (bookmark is null) return;
        var dialog = new EditBookmarkDialog(bookmark) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            AppServices.Bookmarks.SaveAndNotify();
            UpdateAllBookmarkStates();
            ShowToast("书签已更新");
            _ = InternalPageBridge.NotifyAsync(this, "bookmarksChanged", null);
        }
    }

    private void BookmarkMenu_Delete(object sender, RoutedEventArgs e)
    {
        var b = BookmarkFromMenuItem(sender);
        if (b is null) return;
        AppServices.Bookmarks.Remove(b);
        ShowToast("已删除书签");
        _ = InternalPageBridge.NotifyAsync(this, "bookmarksChanged", null);
    }

    private void UpdateAllBookmarkStates()
    {
        foreach (var tab in Tabs)
        {
            var url = GetBookmarkUrl(tab);
            tab.IsBookmarked = url is not null && AppServices.Bookmarks.Contains(url);
        }
        UpdateBookmarkState();
    }

    private void ToggleBookmarksBar()
    {
        Settings.ShowBookmarksBar = !Settings.ShowBookmarksBar;
        AppServices.Settings.RaiseChanged();
        ShowToast(Settings.ShowBookmarksBar ? "已显示书签栏" : "已隐藏书签栏");
    }

    private void PopupButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var popup = sender switch
        {
            _ when ReferenceEquals(sender, MenuButton) => MainMenuPopup,
            _ when ReferenceEquals(sender, ProfileButton) => ProfilePopup,
            _ when ReferenceEquals(sender, DownloadsButton) => DownloadsPopup,
            _ when ReferenceEquals(sender, ExtensionsButton) => ExtensionsPopup,
            _ => null
        };
        if (popup?.IsOpen == true)
            popup.StaysOpen = true;
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (MainMenuPopup.IsOpen)
        {
            ClosePopupAnimated(MainMenuPopup);
            return;
        }
        ProfilePopup.IsOpen = false;
        DownloadsPopup.IsOpen = false;
        ExtensionsPopup.IsOpen = false;
        PreparePopupBackdrop(MainMenuPopup);
        MainMenuPopup.IsOpen = true;
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilePopup.IsOpen)
        {
            ClosePopupAnimated(ProfilePopup);
            return;
        }
        ClosePopupAnimated(MainMenuPopup);
        DownloadsPopup.IsOpen = false;
        ExtensionsPopup.IsOpen = false;
        PreparePopupBackdrop(ProfilePopup);
        ProfilePopup.IsOpen = true;
    }

    private void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadsPopup.IsOpen)
        {
            ClosePopupAnimated(DownloadsPopup);
            return;
        }
        ClosePopupAnimated(MainMenuPopup);
        ProfilePopup.IsOpen = false;
        ExtensionsPopup.IsOpen = false;
        PreparePopupBackdrop(DownloadsPopup);
        DownloadsPopup.IsOpen = true;
    }

    private const string DarkReaderEnableScript =
        "(function(){try{if(window.DarkReader){window.DarkReader.enable({brightness:100,contrast:100,sepia:0,grayscale:0});}}catch(e){}})();";

    private const string DarkReaderDisableScript =
        "(function(){try{if(window.DarkReader){window.DarkReader.disable();}}catch(e){}})();";

    private void AdBlockToggle_Click(object sender, RoutedEventArgs e) => ToggleAdBlock();

    private void DarkReaderToggle_Click(object sender, RoutedEventArgs e) => ToggleDarkReader();

    private void ToggleDarkReader()
    {
        Settings.DarkReaderEnabled = !Settings.DarkReaderEnabled;
        AppServices.Settings.RaiseChanged();
        ShowToast(Settings.DarkReaderEnabled ? "Dark Reader 已开启" : "Dark Reader 已关闭");
    }

    private async Task ApplyDarkReaderStateAsync()
    {
        var enabled = Settings.DarkReaderEnabled;
        foreach (var tab in Tabs)
        {
            if (tab.DarkReaderMenuItem is not null)
                tab.DarkReaderMenuItem.IsChecked = enabled;
            if (tab.Core is null) continue;

            try
            {
                if (!enabled)
                    await tab.Core.ExecuteScriptAsync(DarkReaderDisableScript);
                else
                    await ApplyDarkReaderToPageAsync(tab);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Dark Reader state update failed: " + ex.Message);
            }
        }
    }

    private async Task ApplyDarkReaderToPageAsync(BrowserTab tab)
    {
        if (!Settings.DarkReaderEnabled || tab.Core is null) return;
        if (!IsDarkReaderPage(tab.Core.Source)) return;
        if (!await EnsureDarkReaderInstalledAsync(tab)) return;

        try
        {
            await tab.Core.ExecuteScriptAsync(DarkReaderEnableScript);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Dark Reader enable failed: " + ex.Message);
        }
    }

    private async Task<bool> EnsureDarkReaderInstalledAsync(BrowserTab tab)
    {
        if (tab.DarkReaderInstalled) return true;
        if (tab.Core is null) return false;

        try
        {
            if (_darkReaderSource is null)
            {
                if (!File.Exists(AppPaths.DarkReaderScript))
                {
                    AppPaths.Log("Dark Reader script is missing.");
                    return false;
                }
                _darkReaderSource = await File.ReadAllTextAsync(AppPaths.DarkReaderScript);
            }

            await tab.Core.AddScriptToExecuteOnDocumentCreatedAsync(_darkReaderSource);
            try
            {
                await tab.Core.ExecuteScriptAsync(_darkReaderSource);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Dark Reader current-page install failed: " + ex.Message);
            }
            tab.DarkReaderInstalled = true;
            return true;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Dark Reader install failed: " + ex.Message);
            return false;
        }
    }

    private static bool IsDarkReaderPage(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme is "http" or "https" && !uri.Host.Equals(UrlHelper.InternalHost, StringComparison.OrdinalIgnoreCase);
    }

    private void ToggleAdBlock()
    {
        var enabled = !Settings.AdBlockEnabled;
        Settings.AdBlockEnabled = enabled;
        AppServices.Settings.RaiseChanged();
        ShowToast(enabled ? "AdGuard 广告过滤已开启" : "AdGuard 广告过滤已关闭");
    }

    private async Task ApplyAdBlockStateAsync()
    {
        var enabled = Settings.AdBlockEnabled;
        foreach (var tab in Tabs)
        {
            if (tab.AdBlockMenuItem is not null)
                tab.AdBlockMenuItem.IsChecked = enabled;
            if (tab.Core is null) continue;
            try
            {
                if (!enabled)
                {
                    await tab.Core.ExecuteScriptAsync(AdBlockService.RemovalScript);
                }
                else
                {
                    var script = AppServices.AdBlock.GetInjectionScript(tab.Core.Source);
                    if (script is not null)
                        await tab.Core.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                AppPaths.Log("AdGuard page state update failed: " + ex.Message);
            }
        }
    }

    private async void ExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionsPopup.IsOpen)
        {
            ClosePopupAnimated(ExtensionsPopup);
            return;
        }

        ClosePopupAnimated(MainMenuPopup);
        ProfilePopup.IsOpen = false;
        DownloadsPopup.IsOpen = false;
        PreparePopupBackdrop(ExtensionsPopup);
        ExtensionsPopup.IsOpen = true;
        await RefreshExtensionsAsync();
    }

    private async Task RefreshExtensionsAsync()
    {
        if (IsExtensionsLoading) return;

        ExtensionsError = "";
        IsExtensionsLoading = true;
        Extensions.Clear();
        OnPropertyChanged(nameof(ExtensionsEmptyVisibility));

        try
        {
            if (!Settings.EnableExtensions) return;

            var core = ActiveTab?.Core ?? Tabs.FirstOrDefault(t => t.Core is not null)?.Core;
            if (core is null)
                throw new InvalidOperationException("浏览器尚未准备好。");

            var list = await core.Profile.GetBrowserExtensionsAsync();
            foreach (var extension in list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Extensions.Add(new ExtensionItem
                {
                    Id = extension.Id,
                    Name = extension.Name,
                    IsEnabled = extension.IsEnabled
                });
            }
        }
        catch (Exception ex)
        {
            ExtensionsError = ex.Message;
            AppPaths.Log("Extensions menu load failed: " + ex);
        }
        finally
        {
            IsExtensionsLoading = false;
            OnPropertyChanged(nameof(ExtensionsEmptyVisibility));
        }
    }

    private async void ExtensionToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle || toggle.DataContext is not ExtensionItem item) return;

        var desired = toggle.IsChecked == true;
        item.IsEnabled = desired;
        toggle.IsEnabled = false;
        try
        {
            var updated = await SetExtensionEnabledAsync(item.Id, desired);
            if (!updated)
                throw new InvalidOperationException("无法更新扩展状态。");
        }
        catch (Exception ex)
        {
            item.IsEnabled = !desired;
            toggle.IsChecked = !desired;
            ShowToast("更新扩展状态失败：" + ex.Message);
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private async void ExtensionsLoad_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.IsOpen = false;
        try
        {
            await LoadUnpackedExtensionAsync();
        }
        catch (Exception ex)
        {
            if (!ex.Message.Contains("取消", StringComparison.Ordinal))
                ShowToast(ex.Message);
        }
    }

    private async void ExtensionsInstallCrx_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.IsOpen = false;
        try
        {
            await InstallCrxFromDialogAsync();
        }
        catch (Exception ex)
        {
            ShowToast("CRX 安装失败：" + ex.Message);
        }
    }

    private void SecurityButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        if (SiteInfoPopup.IsOpen)
        {
            ClosePopupAnimated(SiteInfoPopup);
            return;
        }
        UpdateSecurityIcon();
        SiteInfoPopup.IsOpen = true;
    }

    private void SiteInfoClearData_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(SiteInfoPopup);
        _ = ClearSiteDataAsync();
    }

    // ----- Menu actions -----

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        OpenNewWindow();
    }

    private void NewIncognitoWindow_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        ProfilePopup.IsOpen = false;
        OpenNewWindow(null, incognito: true);
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        _ = OpenInternalPageAsync("history");
    }

    private void OpenBookmarks_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        _ = OpenInternalPageAsync("bookmarks");
    }

    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        DownloadsPopup.IsOpen = false;
        _ = OpenInternalPageAsync("downloads");
    }

    private void OpenExtensions_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        ExtensionsPopup.IsOpen = false;
        _ = OpenInternalPageAsync("extensions");
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        ProfilePopup.IsOpen = false;
        _ = OpenInternalPageAsync("settings");
    }

    private void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        _ = OpenInternalPageAsync("about");
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void Print_Click(object sender, RoutedEventArgs e) => PrintCurrentPage();

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        OpenFind();
    }

    private void DevTools_Click(object sender, RoutedEventArgs e)
    {
        ClosePopupAnimated(MainMenuPopup);
        OpenDevTools();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
    private void ResetZoom_Click(object sender, RoutedEventArgs e) => ResetZoom();

    private void ZoomIn()
    {
        if (ActiveTab is null) return;
        ActiveTab.SetZoom(ActiveTab.ZoomFactor + 0.1);
        ShowToast($"缩放 {Math.Round(ActiveTab.ZoomFactor * 100)}%");
    }

    private void ZoomOut()
    {
        if (ActiveTab is null) return;
        ActiveTab.SetZoom(ActiveTab.ZoomFactor - 0.1);
        ShowToast($"缩放 {Math.Round(ActiveTab.ZoomFactor * 100)}%");
    }

    private void ResetZoom()
    {
        if (ActiveTab is null) return;
        ActiveTab.SetZoom(1.0);
        ShowToast("缩放 100%");
    }

    private void OpenNewWindow(string? url = null, bool incognito = false, Point? screenPosition = null)
    {
        var left = screenPosition is { } p
            ? p.X - 140
            : Left + 28;
        var top = screenPosition is { } q
            ? q.Y - 24
            : Top + 28;

        var window = new MainWindow(url, incognito, isPrimary: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft + 20, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 480),
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop + 20, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 320)
        };
        if (WindowState == WindowState.Maximized && screenPosition is null)
            window.WindowState = WindowState.Maximized;
        window.Show();
    }

    // ----- Find bar -----

    private void OpenFind()
    {
        if (ActiveTab is null) return;
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
        if (!string.IsNullOrEmpty(FindBox.Text)) _ = RunFindAsync(true, true);
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        _findVersion++;
        if (ActiveTab?.Core is { } core)
        {
            try { _ = core.ExecuteScriptAsync("window.__xrFind && window.__xrFind.clear();"); }
            catch { }
        }
        try { ActiveTab?.WebView.Focus(); } catch { }
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FindBar.Visibility != Visibility.Visible) return;
        _findTimer.Stop();
        _findTimer.Start();
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = RunFindAsync(false, (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => _ = RunFindAsync(false, true);
    private void FindPrevious_Click(object sender, RoutedEventArgs e) => _ = RunFindAsync(false, false);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

    private async Task RunFindAsync(bool reset, bool forward)
    {
        var tab = ActiveTab;
        if (tab?.Core is null || FindBar.Visibility != Visibility.Visible) return;
        var query = FindBox.Text;
        var version = ++_findVersion;
        try
        {
            string? result;
            if (string.IsNullOrEmpty(query))
            {
                result = await tab.Core.ExecuteScriptAsync("window.__xrFind ? window.__xrFind.clear() : '0/0'");
            }
            else if (reset)
            {
                var json = JsonSerializer.Serialize(query);
                result = await tab.Core.ExecuteScriptAsync($"window.__xrFind ? window.__xrFind.search({json}) : '0/0'");
            }
            else
            {
                result = await tab.Core.ExecuteScriptAsync($"window.__xrFind ? window.__xrFind.next({(forward ? 1 : -1)}) : '0/0'");
            }

            if (version != _findVersion || !ReferenceEquals(tab, ActiveTab)) return;
            var label = "0/0";
            if (!string.IsNullOrWhiteSpace(result))
            {
                try { label = JsonSerializer.Deserialize<string>(result) ?? "0/0"; }
                catch { label = "0/0"; }
            }
            FindResult.Text = label;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Find failed: " + ex.Message);
            FindResult.Text = "0/0";
        }
    }

    // ----- Full screen -----

    private void ToggleFullScreen()
    {
        if (_fullscreen) ExitFullScreen();
        else EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        if (_fullscreen) return;
        _fullscreen = true;
        _preFullscreenState = WindowState;
        _preFullscreenRect = new Rect(Left, Top, Width, Height);
        if (FindBar.Visibility == Visibility.Visible) CloseFind();
        ChromeRoot.Visibility = Visibility.Collapsed;
        WindowFrame.BorderThickness = new Thickness(0);
        WindowState = WindowState.Maximized;
        ShowToast("按 F11 或 Esc 退出全屏");
    }

    private void ExitFullScreen()
    {
        if (!_fullscreen) return;
        _fullscreen = false;
        ChromeRoot.Visibility = Visibility.Visible;
        WindowFrame.BorderThickness = new Thickness(1);
        WindowState = _preFullscreenState;
        if (_preFullscreenState == WindowState.Normal && _preFullscreenRect != Rect.Empty)
        {
            Left = _preFullscreenRect.Left;
            Top = _preFullscreenRect.Top;
            Width = _preFullscreenRect.Width;
            Height = _preFullscreenRect.Height;
        }
    }

    // ----- Settings/theme -----

    private void Settings_Changed(object? sender, EventArgs e)
    {
        ApplyStaticSettings();
        if (_appliedTheme != Settings.Theme)
        {
            _appliedTheme = Settings.Theme;
            ThemeService.Apply(Settings.Theme);
        }
        ApplyBrowserTheme();
        OnPropertyChanged(nameof(AdBlockEnabled));
        OnPropertyChanged(nameof(DarkReaderEnabled));
        if (_lastAdBlockEnabled != Settings.AdBlockEnabled)
        {
            _lastAdBlockEnabled = Settings.AdBlockEnabled;
            _ = ApplyAdBlockStateAsync();
        }
        if (_lastDarkReaderEnabled != Settings.DarkReaderEnabled)
        {
            _lastDarkReaderEnabled = Settings.DarkReaderEnabled;
            _ = ApplyDarkReaderStateAsync();
        }
        if (_lastMicaEnabled != Settings.MicaEnabled || _lastGlassEnabled != Settings.GlassEnabled)
        {
            _lastMicaEnabled = Settings.MicaEnabled;
            _lastGlassEnabled = Settings.GlassEnabled;
            ApplyMica();
        }
        else if (Math.Abs(_lastSurfaceOpacity - Settings.SurfaceOpacity) > 0.001)
        {
            _lastSurfaceOpacity = Settings.SurfaceOpacity;
            ApplyMica();
        }
        OnPropertyChanged(nameof(ExtensionsCanInstall));
        OnPropertyChanged(nameof(ExtensionsDisabledVisibility));
        OnPropertyChanged(nameof(ExtensionsEmptyVisibility));
        if (ExtensionsPopup.IsOpen)
            _ = RefreshExtensionsAsync();
        ScheduleSessionSave();
        _ = InternalPageBridge.NotifyAsync(this, "settingsChanged", null);
    }

    private void Theme_Changed(object? sender, EventArgs e)
    {
        ApplyBrowserTheme();
        ApplyMica();
        _ = InternalPageBridge.NotifyAsync(this, "themeChanged", ThemeService.IsDark);
    }

    private void ApplyMica()
    {
        if (!_loaded) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyMica(this, false);
        NativeMethods.ClearAcrylic(hwnd);

        SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");
        WindowFrame.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        TabStripPanel.SetResourceReference(Panel.BackgroundProperty, "TabStripBrush");
        ToolbarPanel.SetResourceReference(Panel.BackgroundProperty, "ToolbarBrush");
        BookmarksBar.SetResourceReference(Panel.BackgroundProperty, "ToolbarBrush");
    }

    private void ApplyBrowserTheme()
    {
        if (!_loaded) return;
        var color = GetContentBackgroundColor();
        foreach (var tab in Tabs)
        {
            tab.SetBackground(color);
            try
            {
                if (tab.Core?.Profile is { } profile)
                    profile.PreferredColorScheme = ThemeService.IsDark
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;
            }
            catch { }
        }
        NativeMethods.ApplyDarkFrame(this, ThemeService.IsDark);
    }

    private static Color GetContentBackgroundColor()
    {
        if (Application.Current.TryFindResource("ContentBackgroundBrush") is SolidColorBrush brush)
            return brush.Color;
        return Colors.White;
    }

    private void Downloads_Changed(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ActiveDownloadCount));
        OnPropertyChanged(nameof(HasDownloads));
        _ = InternalPageBridge.NotifyAsync(this, "downloadsChanged", null);
    }

    private void Downloads_Completed(object? sender, DownloadItem item)
    {
        ShowToast($"下载完成：{item.FileName}");
        OnPropertyChanged(nameof(ActiveDownloadCount));
    }

    // ----- Toast -----

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastBorder.BeginAnimation(OpacityProperty, null);
        ToastBorder.Opacity = 0;
        ToastPopup.IsOpen = true;
        ToastBorder.UpdateLayout();
        UpdateOverlayPositions();
        Dispatcher.BeginInvoke(new Action(UpdateOverlayPositions), DispatcherPriority.Loaded);
        ToastBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        animation.Completed += (_, _) => ToastPopup.IsOpen = false;
        ToastBorder.BeginAnimation(OpacityProperty, animation);
    }

    private void ContentRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOverlayPositions();
    }

    private void UpdateOverlayPositions()
    {
        if (StatusPopup.IsOpen)
        {
            StatusPill.UpdateLayout();
            var height = StatusPill.ActualHeight > 0 ? StatusPill.ActualHeight : 26;
            StatusPopup.HorizontalOffset = 14;
            StatusPopup.VerticalOffset = Math.Max(8, ContentRoot.ActualHeight - height - 12);
        }
        if (ToastPopup.IsOpen)
        {
            ToastBorder.UpdateLayout();
            var width = ToastBorder.ActualWidth > 0 ? ToastBorder.ActualWidth : ToastBorder.DesiredSize.Width;
            var height = ToastBorder.ActualHeight > 0 ? ToastBorder.ActualHeight : ToastBorder.DesiredSize.Height;
            ToastPopup.HorizontalOffset = Math.Max(14, (ContentRoot.ActualWidth - width) / 2);
            ToastPopup.VerticalOffset = Math.Max(14, ContentRoot.ActualHeight - height - 24);
        }
    }

    private void CrashReload_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        ActiveTab.IsCrashed = false;
        UpdateCrashOverlay();
        ActiveTab.Reload();
    }

    private void CrashClose_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is not null) CloseTab(ActiveTab);
    }

    // ----- Session -----

    private void ScheduleSessionSave()
    {
        if (!_isPrimary || _isIncognito || !_tabsReady || _closing) return;
        _sessionTimer.Stop();
        _sessionTimer.Start();
    }

    private void SaveSessionNow()
    {
        if (!_isPrimary || _isIncognito || !_tabsReady) return;
        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            var session = new SessionState
            {
                ActiveIndex = ActiveTab is null ? 0 : Math.Max(0, Tabs.IndexOf(ActiveTab)),
                Width = bounds.Width > 0 ? bounds.Width : Width,
                Height = bounds.Height > 0 ? bounds.Height : Height,
                Left = bounds.Left,
                Top = bounds.Top,
                Maximized = WindowState == WindowState.Maximized,
                Tabs = Tabs.Select(t => new SessionTab
                {
                    Url = UrlHelper.ToCanonicalUrl(t.Url),
                    Title = t.Title
                }).ToList()
            };
            AppServices.Sessions.Save(session);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Session save failed: " + ex.Message);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
