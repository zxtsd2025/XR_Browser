using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using XRBrowser.Dialogs;
using XRBrowser.Models;
using XRBrowser.Services;

namespace XRBrowser;

public partial class MainWindow
{
    public async Task<BrowserTab> CreateTabAsync(string? url, bool activate = true, bool navigate = true, int? index = null)
    {
        if (_environment is null)
            throw new InvalidOperationException("The browser environment is not ready.");

        AppPaths.Log("CreateTabAsync url=" + (url ?? "<new>") + " activate=" + activate + " navigate=" + navigate);
        var tab = new BrowserTab(_isIncognito);
        tab.PropertyChanged += Tab_PropertyChanged;
        tab.FaviconUpdated += Tab_FaviconUpdated;
        tab.FullScreenChanged += Tab_FullScreenChanged;

        var insertIndex = index is null ? Tabs.Count : Math.Clamp(index.Value, 0, Tabs.Count);
        Tabs.Insert(insertIndex, tab);
        TabsHost.Children.Insert(insertIndex, tab.WebView);
        tab.SetBackground(GetContentBackgroundColor());

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            TabsHost.UpdateLayout();
            AppPaths.Log($"Tab host after layout: loaded={tab.WebView.IsLoaded}, actual={tab.WebView.ActualWidth}x{tab.WebView.ActualHeight}, host={TabsHost.ActualWidth}x{TabsHost.ActualHeight}");
            await tab.InitializeAsync(_environment, Settings);
            AppPaths.Log("Tab initialized, configuring core");
            await ConfigureTabAsync(tab);
            AppPaths.Log("Tab core configured");
            var bookmarkUrl = GetBookmarkUrl(tab);
            tab.IsBookmarked = bookmarkUrl is not null && AppServices.Bookmarks.Contains(bookmarkUrl);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Tab initialization failed: " + ex);
            if (TabsHost.Children.Contains(tab.WebView)) TabsHost.Children.Remove(tab.WebView);
            Tabs.Remove(tab);
            tab.PropertyChanged -= Tab_PropertyChanged;
            tab.FaviconUpdated -= Tab_FaviconUpdated;
            tab.FullScreenChanged -= Tab_FullScreenChanged;
            tab.Dispose();
            ShowToast("无法创建标签页：" + ex.Message);
            if (Tabs.Count == 0)
            {
                StartupOverlay.Visibility = Visibility.Visible;
                StartupOverlay.Children.Clear();
                var error = new TextBlock
                {
                    Text = "无法启动 Chromium 页面\n" + ex.Message,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 13,
                    MaxWidth = 620,
                    Margin = new Thickness(40)
                };
                StartupOverlay.Children.Add(error);
            }
            throw;
        }

        if (navigate)
        {
            var target = string.IsNullOrWhiteSpace(url) ? UrlHelper.NewTabUrl : url;
            NavigateTab(tab, target);
        }

        if (activate) SelectTab(tab);
        else
        {
            tab.SetActive(false);
            tab.WebView.Visibility = Visibility.Collapsed;
        }

        StartupOverlay.Visibility = Visibility.Collapsed;
        UpdateTabWidth();
        ScheduleSessionSave();
        return tab;
    }

    private async Task ConfigureTabAsync(BrowserTab tab)
    {
        var core = tab.Core;
        if (core is null) return;

        try
        {
            var themeScript = $"window.__XR_INITIAL_THEME__='{(ThemeService.IsDark ? "dark" : "light")}';";
            await core.AddScriptToExecuteOnDocumentCreatedAsync(themeScript);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(FindScript.JavaScript);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Page script injection failed: " + ex.Message);
        }

        try
        {
            core.Profile.PreferredColorScheme = ThemeService.IsDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
            core.Profile.DefaultDownloadFolderPath = Settings.DownloadDirectory;
        }
        catch { }

        core.NavigationStarting += (_, e) => Core_NavigationStarting(tab, e);
        core.NavigationCompleted += (_, e) => Core_NavigationCompleted(tab, e);
        core.SourceChanged += (_, _) => Core_SourceChanged(tab);
        core.DocumentTitleChanged += (_, _) => Core_DocumentTitleChanged(tab);
        core.StatusBarTextChanged += (_, _) => Core_StatusBarTextChanged(tab);
        core.NewWindowRequested += (_, e) => Core_NewWindowRequested(tab, e);
        core.WindowCloseRequested += (_, _) => CloseTab(tab);
        core.DownloadStarting += (_, e) => Core_DownloadStarting(tab, e);
        core.PermissionRequested += (_, e) => Core_PermissionRequested(tab, e);
        core.ProcessFailed += (_, e) => Core_ProcessFailed(tab, e);
        core.WebMessageReceived += (_, e) => Core_WebMessageReceived(tab, e);
        tab.WebView.GotFocus += (_, _) => CloseToolbarPopups();
        tab.WebView.PreviewMouseDown += (_, _) => CloseToolbarPopups();
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => Core_WebResourceRequested(tab, e);
        core.ContentLoading += (_, _) => Core_ContentLoading(tab);
        if (Settings.DarkReaderEnabled)
            await EnsureDarkReaderInstalledAsync(tab);
        ConfigureAdBlockContextMenu(tab, core);
    }

    private void Core_WebResourceRequested(BrowserTab tab, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Settings.AdBlockEnabled || _environment is null) return;
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document) return;

        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var requestUri)) return;
        Uri? documentUri = null;
        var documentUrl = tab.Core?.Source;
        if (!string.IsNullOrWhiteSpace(documentUrl))
            Uri.TryCreate(documentUrl, UriKind.Absolute, out documentUri);

        var resourceType = MapAdBlockResourceType(e.ResourceContext);
        if (!AppServices.AdBlock.ShouldBlock(requestUri, documentUri, resourceType)) return;

        try
        {
            e.Response = _environment.CreateWebResourceResponse(
                null, 403, "Blocked by XR AdGuard", "Content-Type: text/plain\r\n");
        }
        catch (Exception ex)
        {
            AppPaths.Log("AdGuard block response failed: " + ex.Message);
        }
    }

    private async void Core_ContentLoading(BrowserTab tab) => await InjectAdBlockCssAsync(tab);

    private async Task InjectAdBlockCssAsync(BrowserTab tab)
    {
        if (!Settings.AdBlockEnabled) return;
        try
        {
            var core = tab.Core;
            if (core is null) return;
            var script = AppServices.AdBlock.GetInjectionScript(core.Source);
            if (script is not null)
                await core.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            AppPaths.Log("AdGuard cosmetic injection failed: " + ex.Message);
        }
    }

    private void ConfigureAdBlockContextMenu(BrowserTab tab, CoreWebView2 core)
    {
        core.ContextMenuRequested += (_, e) => ShowPageContextMenu(tab, e);
    }

    private void ShowPageContextMenu(BrowserTab tab, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var core = tab.Core;
        if (core is null) return;
        e.Handled = true;

        var target = e.ContextMenuTarget;
        var menu = new ContextMenu
        {
            PlacementTarget = tab.WebView,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false
        };

        void Add(string header, Action action, bool enabled = true, string gesture = "")
        {
            var item = new MenuItem
            {
                Header = header,
                InputGestureText = gesture,
                IsEnabled = enabled
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("后退", () => core.GoBack(), core.CanGoBack, "Alt+←");
        Add("前进", () => core.GoForward(), core.CanGoForward, "Alt+→");
        Add("重新加载", () => core.Reload(), true, "Ctrl+R");
        menu.Items.Add(new Separator());

        if (target.HasLinkUri && !string.IsNullOrWhiteSpace(target.LinkUri))
        {
            var link = target.LinkUri;
            Add("在新标签页中打开链接", () => _ = CreateTabAsync(link, true));
            Add("复制链接地址", () => CopyTextToClipboard(link));
            menu.Items.Add(new Separator());
        }

        if (target.HasSelection)
        {
            Add("复制", () => _ = RunPageCommandAsync(tab, "document.execCommand('copy')"));
        }
        if (target.IsEditable)
        {
            Add("剪切", () => _ = RunPageCommandAsync(tab, "document.execCommand('cut')"));
            Add("粘贴", () => _ = PasteIntoPageAsync(tab));
            Add("全选", () => _ = RunPageCommandAsync(tab, "document.execCommand('selectAll')"));
        }
        else
        {
            Add("全选", () => _ = RunPageCommandAsync(tab, "document.execCommand('selectAll')"));
        }

        menu.Items.Add(new Separator());
        Add($"AdGuard 广告过滤：{(Settings.AdBlockEnabled ? "开" : "关")}", ToggleAdBlock);
        Add($"Dark Reader：{(Settings.DarkReaderEnabled ? "开" : "关")}", ToggleDarkReader);
        menu.Items.Add(new Separator());
        Add("打印…", PrintCurrentPage, true, "Ctrl+P");
        Add("检查", OpenDevTools, true, "F12");

        menu.MinWidth = 250;
        menu.Measure(new Size(290, double.PositiveInfinity));
        var menuSize = new Size(
            Math.Max(250, Math.Min(300, menu.DesiredSize.Width)),
            Math.Max(140, menu.DesiredSize.Height));
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var dpi = VisualTreeHelper.GetDpi(menu).DpiScaleX;
            var backdrop = CreateBackdropBrush(cursor.X - 8, cursor.Y - 8, menuSize, dpi);
            if (backdrop is not null) menu.Background = backdrop;
        }

        PrepareAnimatedContextMenu(menu);
        menu.IsOpen = true;
    }

    private void PrepareAnimatedContextMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            ApplyContextMenuMaterial(menu);
            if (!Settings.AnimationsEnabled) return;

            menu.RenderTransformOrigin = new Point(0, 0);
            var scale = new ScaleTransform(0.96, 0.96);
            menu.RenderTransform = scale;
            var spring = new SpringAnimation((progress, velocity) =>
            {
                menu.Opacity = Math.Clamp(progress, 0, 1);
                var value = 0.96 + 0.04 * Math.Clamp(progress, 0, 1.02);
                scale.ScaleX = value;
                scale.ScaleY = value;
                ApplyMotionBlur(menu, velocity, 1.8, 0.2);
            }, () =>
            {
                menu.Opacity = 1;
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                ApplyMotionBlur(menu, 0, 1.8, 0.2);
            }, stiffness: 280, damping: 26, speed: Settings.AnimationSpeed);
            spring.Start(0, 1);
        };
    }

    private void ApplyContextMenuMaterial(ContextMenu menu)
    {
        var opacity = Math.Clamp(Settings.SurfaceOpacity + 0.10, 0.5, 0.94);
        if (menu.Background is not DrawingBrush &&
            FindResource("MenuBackgroundBrush") is SolidColorBrush baseBrush)
        {
            var color = baseBrush.Color;
            menu.Background = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(255 * opacity), color.R, color.G, color.B));
        }

    }

    private Brush? CreateBackdropBrush(
        int screenX, int screenY, Size size, double dpi,
        byte tintAlpha = 145, double groupOpacity = -1)
    {
        try
        {
            dpi = Math.Clamp(dpi, 0.5, 3.0);
            var width = Math.Max(1, (int)Math.Round(size.Width * dpi));
            var height = Math.Max(1, (int)Math.Round(size.Height * dpi));
            var screenWidth = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi);
            var screenHeight = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi);
            var x = Math.Clamp(screenX, 0, Math.Max(0, screenWidth - width));
            var y = Math.Clamp(screenY, 0, Math.Max(0, screenHeight - height));

            var source = NativeMethods.CaptureScreen(x, y, width, height);
            if (source is null) return null;

            // Two-pass Gaussian blur gives a softer, more expensive-looking
            // backdrop than a single 18px BlurEffect. The second pass operates
            // on the already blurred intermediate frame.
            var blurred = RenderBlurred(source, size, width, height, 24);
            var target = RenderBlurred(blurred, size, width, height, 15);

            var tint = ThemeService.IsDark
                ? System.Windows.Media.Color.FromArgb(tintAlpha, 0x20, 0x21, 0x24)
                : System.Windows.Media.Color.FromArgb(tintAlpha, 0xF5, 0xF6, 0xF8);
            var drawing = new DrawingGroup
            {
                Opacity = groupOpacity > 0
                    ? groupOpacity
                    : Math.Clamp(Settings.SurfaceOpacity + 0.35, 0.72, 0.98)
            };
            drawing.Children.Add(new ImageDrawing(target, new Rect(0, 0, size.Width, size.Height)));
            drawing.Children.Add(new GeometryDrawing(
                new SolidColorBrush(tint), null, new RectangleGeometry(new Rect(0, 0, size.Width, size.Height))));

            var sheen = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(30, 255, 255, 255),
                System.Windows.Media.Color.FromArgb(0, 255, 255, 255),
                90);
            drawing.Children.Add(new GeometryDrawing(
                sheen, null, new RectangleGeometry(new Rect(0, 0, size.Width, size.Height))));

            var brush = new DrawingBrush(drawing)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, size.Width, size.Height),
                Viewport = new Rect(0, 0, size.Width, size.Height)
            };
            brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Context backdrop capture failed: " + ex.Message);
            return null;
        }
    }

    private static BitmapSource RenderBlurred(
        ImageSource source, Size size, int pixelWidth, int pixelHeight, double radius)
    {
        var image = new Image
        {
            Source = source,
            Width = size.Width,
            Height = size.Height,
            Stretch = Stretch.Fill,
            Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = radius,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
            }
        };
        image.Measure(size);
        image.Arrange(new Rect(size));
        var target = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(image);
        target.Freeze();
        return target;
    }

    private async Task RunPageCommandAsync(BrowserTab tab, string script)
    {
        try
        {
            if (tab.Core is not null)
                await tab.Core.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Page command failed: " + ex.Message);
        }
    }

    private async Task PasteIntoPageAsync(BrowserTab tab)
    {
        try
        {
            if (!Clipboard.ContainsText() || tab.Core is null) return;
            var text = Clipboard.GetText();
            var json = JsonSerializer.Serialize(text);
            await tab.Core.ExecuteScriptAsync(
                "(function(){const t=" + json + ";" +
                "const el=document.activeElement;" +
                "if(el&&(el.tagName==='INPUT'||el.tagName==='TEXTAREA'||el.isContentEditable)){" +
                "document.execCommand('insertText',false,t);}else{document.execCommand('insertText',false,t);}})();");
        }
        catch (Exception ex)
        {
            AppPaths.Log("Paste failed: " + ex.Message);
        }
    }

    private static void CopyTextToClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }

    private static AdBlockResourceType MapAdBlockResourceType(CoreWebView2WebResourceContext context)
        => context switch
        {
            CoreWebView2WebResourceContext.Document => AdBlockResourceType.Document,
            CoreWebView2WebResourceContext.Stylesheet => AdBlockResourceType.Stylesheet,
            CoreWebView2WebResourceContext.Image => AdBlockResourceType.Image,
            CoreWebView2WebResourceContext.Media => AdBlockResourceType.Media,
            CoreWebView2WebResourceContext.Font => AdBlockResourceType.Font,
            CoreWebView2WebResourceContext.Script => AdBlockResourceType.Script,
            CoreWebView2WebResourceContext.XmlHttpRequest => AdBlockResourceType.Xhr,
            _ => AdBlockResourceType.Other
        };

    private void Core_NavigationStarting(BrowserTab tab, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            !UrlHelper.IsSupportedBrowserScheme(uri.Scheme))
        {
            e.Cancel = true;
            LaunchExternal(uri.AbsoluteUri);
            return;
        }

        if (tab.IsCrashed)
        {
            tab.IsCrashed = false;
            if (ReferenceEquals(tab, ActiveTab)) UpdateCrashOverlay();
        }
        if (ReferenceEquals(tab, ActiveTab))
        {
            OmniboxPopup.IsOpen = false;
            UpdateNavigationState();
        }
        StatusPopup.IsOpen = false;
    }

    private void Core_NavigationCompleted(BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (ReferenceEquals(tab, ActiveTab))
        {
            UpdateNavigationState();
            UpdateAddressBar();
            UpdateSecurityIcon();
            UpdateBookmarkState();
            UpdateWindowTitle();
        }

        if (e.IsSuccess)
        {
            if (!_isIncognito)
                AppServices.History.AddVisit(tab.Url, tab.Title);
            if (FindBar.Visibility == Visibility.Visible && !string.IsNullOrEmpty(FindBox.Text) &&
                ReferenceEquals(tab, ActiveTab))
                _ = RunFindAsync(true, true);
        }
        _ = InjectAdBlockCssAsync(tab);
        _ = ApplyDarkReaderToPageAsync(tab);
        ScheduleSessionSave();
    }

    private void Core_SourceChanged(BrowserTab tab)
    {
        var core = tab.Core;
        if (core is null) return;

        if (UrlHelper.IsNewTabUrl(core.Source))
        {
            tab.Title = "新标签页";
        }
        else if (UrlHelper.IsInternalAppUrl(core.Source))
        {
            tab.Title = UrlHelper.GetInternalPageName(core.Source) switch
            {
                "history" => "历史记录",
                "bookmarks" => "书签",
                "downloads" => "下载内容",
                "extensions" => "扩展程序",
                "about" => "关于 XR Browser",
                _ => "设置"
            };
        }

        if (ReferenceEquals(tab, ActiveTab))
        {
            UpdateAddressBar();
            UpdateSecurityIcon();
            UpdateBookmarkState();
            UpdateWindowTitle();
        }
        ScheduleSessionSave();
    }

    private void Core_DocumentTitleChanged(BrowserTab tab)
    {
        if (!string.IsNullOrWhiteSpace(tab.Title) && !UrlHelper.IsInternalUrl(tab.Url))
            AppServices.History.UpdateTitle(tab.Url, tab.Title);
        if (ReferenceEquals(tab, ActiveTab)) UpdateWindowTitle();
        ScheduleSessionSave();
    }

    private void Core_StatusBarTextChanged(BrowserTab tab)
    {
        if (!ReferenceEquals(tab, ActiveTab)) return;
        var text = tab.Core?.StatusBarText;
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusPopup.IsOpen = false;
            return;
        }
        StatusText.Text = text;
        StatusPopup.IsOpen = true;
        StatusPill.UpdateLayout();
        UpdateOverlayPositions();
    }

    private async void Core_NewWindowRequested(BrowserTab source, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            !UrlHelper.IsSupportedBrowserScheme(uri.Scheme))
        {
            e.Handled = true;
            LaunchExternal(uri.AbsoluteUri);
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var tab = await CreateTabAsync(null, activate: true, navigate: false);
            if (tab.Core is not null)
            {
                e.NewWindow = tab.Core;
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("New window failed: " + ex);
            e.Handled = true;
            _ = CreateTabAsync(e.Uri, activate: true);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Core_DownloadStarting(BrowserTab tab, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Handled = true;
        try
        {
            AppPaths.Log($"DownloadStarting path={e.ResultFilePath} ask={Settings.AskWhereToSave}");
            var operation = e.DownloadOperation;
            if (operation is null) return;

            var suggested = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggested))
            {
                try { suggested = Path.GetFileName(new Uri(operation.Uri).LocalPath); }
                catch { suggested = "download"; }
            }
            if (string.IsNullOrWhiteSpace(suggested)) suggested = "download";

            string path;
            if (Settings.AskWhereToSave)
            {
                var extension = Path.GetExtension(suggested);
                var dialog = new SaveFileDialog
                {
                    Title = "保存下载文件",
                    FileName = suggested,
                    InitialDirectory = Settings.DownloadDirectory,
                    DefaultExt = extension,
                    Filter = string.IsNullOrWhiteSpace(extension)
                        ? "所有文件 (*.*)|*.*"
                        : $"{extension.TrimStart('.').ToUpperInvariant()} 文件 (*{extension})|*{extension}|所有文件 (*.*)|*.*",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog(this) != true)
                {
                    e.Cancel = true;
                    return;
                }
                path = dialog.FileName;
            }
            else
            {
                path = DownloadService.BuildUniquePath(Settings.DownloadDirectory, suggested);
            }

            e.ResultFilePath = path;
            var item = AppServices.Downloads.Attach(operation);
            AppPaths.Log($"Download setup complete: {path}");
            ShowToast($"正在下载 {item.FileName}");
        }
        catch (Exception ex)
        {
            AppPaths.Log("Download setup failed: " + ex);
            e.Handled = false;
        }
    }

    private async void Core_PermissionRequested(BrowserTab tab, CoreWebView2PermissionRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var site = "此网站";
            try { site = new Uri(e.Uri).Host; } catch { }

            var (title, description, glyph) = e.PermissionKind switch
            {
                CoreWebView2PermissionKind.Camera => ("使用摄像头", "允许此网站使用你的摄像头？", "\uE722"),
                CoreWebView2PermissionKind.Microphone => ("使用麦克风", "允许此网站使用你的麦克风？", "\uE720"),
                CoreWebView2PermissionKind.Geolocation => ("获取你的位置", "允许此网站获取你的精确位置？", "\uE81D"),
                CoreWebView2PermissionKind.Notifications => ("发送通知", "允许此网站向你发送通知？", "\uE7E7"),
                CoreWebView2PermissionKind.ClipboardRead => ("读取剪贴板", "允许此网站读取你剪贴板中的内容？", "\uE77F"),
                CoreWebView2PermissionKind.MultipleAutomaticDownloads => ("自动下载多个文件", "允许此网站自动下载多个文件？", "\uE896"),
                CoreWebView2PermissionKind.FileReadWrite => ("编辑文件", "允许此网站读取和写入你选择的文件？", "\uE8A5"),
                CoreWebView2PermissionKind.Autoplay => ("自动播放", "允许此网站自动播放媒体？", "\uE768"),
                CoreWebView2PermissionKind.LocalFonts => ("使用本地字体", "允许此网站使用你设备上安装的字体？", "\uE8D2"),
                CoreWebView2PermissionKind.WindowManagement => ("管理窗口", "允许此网站打开和管理窗口？", "\uE737"),
                _ => ("访问设备功能", "允许此网站访问设备功能？", "\uE7BA")
            };

            var dialog = new PermissionDialog(site, title, description, glyph) { Owner = this };
            var allowed = dialog.ShowDialog() == true && dialog.Allowed;
            e.State = allowed ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            e.SavesInProfile = true;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Permission dialog failed: " + ex);
            e.State = CoreWebView2PermissionState.Deny;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Core_ProcessFailed(BrowserTab tab, CoreWebView2ProcessFailedEventArgs e)
    {
        AppPaths.Log($"Process failed for {tab.Url}: {e.ProcessFailedKind} {e.Reason}");
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
            or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
        {
            tab.IsCrashed = true;
            if (ReferenceEquals(tab, ActiveTab))
                UpdateCrashOverlay();
        }
        else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            ShowToast("浏览器进程已退出，请重新启动 XR Browser");
        }
    }

    private async void Core_WebMessageReceived(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var source = tab.Core?.Source ?? "";
            if (!UrlHelper.IsInternalUrl(source)) return;
            await InternalPageBridge.HandleAsync(this, tab, e);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Internal page message failed: " + ex);
        }
    }

    public void NavigateTab(BrowserTab? tab, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) url = "xr://newtab";
        if (UrlHelper.IsInternalInput(url)) url = UrlHelper.ToInternalUrl(url);

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            !UrlHelper.IsSupportedBrowserScheme(uri.Scheme))
        {
            LaunchExternal(uri.AbsoluteUri);
            return;
        }

        if (tab is null)
        {
            _ = CreateTabAsync(url, activate: true);
            return;
        }

        tab.Navigate(url);
        if (!ReferenceEquals(tab, ActiveTab)) SelectTab(tab);
    }

    private void LaunchExternal(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        if (uri.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("出于安全原因，已阻止此链接");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            ShowToast("已使用系统应用打开链接");
        }
        catch (Exception ex)
        {
            AppPaths.Log("External launch failed: " + ex.Message);
            ShowToast("无法打开外部链接");
        }
    }

    public async Task OpenInternalPageAsync(string page)
    {
        var canonical = "xr://" + page.TrimStart('/');
        if (ActiveTab is { IsNewTabPage: true })
        {
            NavigateTab(ActiveTab, canonical);
            return;
        }
        if (ActiveTab is not null && UrlHelper.IsInternalAppUrl(ActiveTab.Url) && !ActiveTab.CanGoBack)
        {
            NavigateTab(ActiveTab, canonical);
            return;
        }
        await CreateTabAsync(canonical, activate: true);
    }

    public void PrintCurrentPage()
    {
        var core = ActiveTab?.Core;
        if (core is null) return;
        try { core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
        catch (Exception ex)
        {
            AppPaths.Log("Print failed, falling back to window.print: " + ex.Message);
            _ = core.ExecuteScriptAsync("window.print()");
        }
    }

    public void OpenDevTools()
    {
        try { ActiveTab?.Core?.OpenDevToolsWindow(); }
        catch (Exception ex) { ShowToast("无法打开开发者工具：" + ex.Message); }
    }

    private async Task ClearSiteDataAsync()
    {
        var tab = ActiveTab;
        if (tab?.Core is null || string.IsNullOrWhiteSpace(tab.Url)) return;
        try
        {
            var uri = new Uri(tab.Url);
            var origin = uri.GetLeftPart(UriPartial.Authority);
            var payload = JsonSerializer.Serialize(new { origin, storageTypes = "all" });
            await tab.Core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin", payload);
            await tab.Core.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
            ShowToast($"已清除 {uri.Host} 的网站数据");
            tab.Reload();
        }
        catch (Exception ex)
        {
            AppPaths.Log("Clear site data failed: " + ex.Message);
            ShowToast("清除网站数据失败");
        }
    }

    private void Download_Open(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DownloadItem item) item.Open();
    }

    private void Download_ShowInFolder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DownloadItem item) item.ShowInFolder();
    }
}
