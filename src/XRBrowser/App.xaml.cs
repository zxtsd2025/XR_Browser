using System.Windows;
using System.Windows.Threading;
using XRBrowser.Services;

namespace XRBrowser;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppPaths.Log("Fatal: " + args.ExceptionObject);

        AppServices.Initialize();
        ThemeService.Apply(AppServices.Settings.Current.Theme);

        var runtime = BrowserEnvironment.GetRuntimeVersionOrNull();
        if (string.IsNullOrEmpty(runtime))
        {
            MessageBox.Show(
                "未检测到 Microsoft Edge WebView2 运行时。\n\n" +
                "XR Browser 使用 Chromium 内核，需要安装 WebView2 Runtime。\n" +
                "请访问 https://go.microsoft.com/fwlink/p/?LinkId=2124703 下载并安装后重试。",
                "XR Browser · 缺少运行时",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var incognito = e.Args.Any(a => a.Equals("--incognito", StringComparison.OrdinalIgnoreCase));
        var initial = e.Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !a.StartsWith("-psn_", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(initial) && !initial.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            !initial.StartsWith("xr://", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(initial))
        {
            initial = new Uri(Path.GetFullPath(initial)).AbsoluteUri;
        }

        AppPaths.Log("App startup, creating main window");
        var window = new MainWindow(initial, incognito, isPrimary: true);
        MainWindow = window;
        window.Show();
        AppPaths.Log("Main window shown");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppPaths.Log("Unhandled UI exception: " + e.Exception);
        e.Handled = true;
        try
        {
            MessageBox.Show(
                "XR Browser 遇到了一个意外错误，但已防止程序退出。\n\n" +
                e.Exception.Message + "\n\n详细信息已写入日志。",
                "XR Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppPaths.Log("App OnExit, code=" + e.ApplicationExitCode);
        AppServices.IsShuttingDown = true;
        try { AppServices.Shutdown(); }
        catch (Exception ex) { AppPaths.Log("Shutdown failed: " + ex); }
        base.OnExit(e);
    }
}
