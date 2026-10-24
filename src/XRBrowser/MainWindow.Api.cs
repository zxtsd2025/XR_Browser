using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using XRBrowser.Models;
using XRBrowser.Services;

namespace XRBrowser;

public partial class MainWindow
{
    public string DataRoot => AppPaths.DataRoot;
    public bool IsPrimaryWindow => _isPrimary;

    internal bool ApplySetting(string key, JsonElement value)
    {
        if (!AppServices.Settings.Set(key, value)) return false;
        AppServices.Settings.RaiseChanged();
        return true;
    }

    internal async Task<bool> ClearBrowsingDataAsync(IReadOnlyCollection<string> kinds)
    {
        var core = ActiveTab?.Core ?? Tabs.FirstOrDefault(t => t.Core is not null)?.Core;
        if (core is null) return false;

        var flags = (CoreWebView2BrowsingDataKinds)0;
        var all = kinds.Count == 0 || kinds.Contains("all", StringComparer.OrdinalIgnoreCase);
        if (all)
        {
            flags = CoreWebView2BrowsingDataKinds.AllProfile;
        }
        else
        {
            foreach (var kind in kinds)
            {
                flags |= kind.ToLowerInvariant() switch
                {
                    "history" => CoreWebView2BrowsingDataKinds.BrowsingHistory,
                    "cookies" => CoreWebView2BrowsingDataKinds.Cookies | CoreWebView2BrowsingDataKinds.AllDomStorage,
                    "cache" => CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage,
                    "passwords" => CoreWebView2BrowsingDataKinds.PasswordAutosave,
                    "autofill" => CoreWebView2BrowsingDataKinds.GeneralAutofill,
                    "downloads" => CoreWebView2BrowsingDataKinds.DownloadHistory,
                    "siteSettings" => CoreWebView2BrowsingDataKinds.Settings,
                    "serviceWorkers" => CoreWebView2BrowsingDataKinds.ServiceWorkers,
                    _ => 0
                };
            }
        }

        if (flags == 0) return false;

        try
        {
            await core.Profile.ClearBrowsingDataAsync(flags);
            if (all || kinds.Contains("history", StringComparer.OrdinalIgnoreCase))
                AppServices.History.Clear();
            if (all || kinds.Contains("downloads", StringComparer.OrdinalIgnoreCase))
                AppServices.Downloads.ClearFinished();
            ShowToast("已清除浏览数据");
            return true;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Clear browsing data failed: " + ex.Message);
            ShowToast("清除浏览数据失败");
            return false;
        }
    }

    internal Task<string?> ChooseDownloadFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择下载文件夹",
            InitialDirectory = Settings.DownloadDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return Task.FromResult<string?>(null);
        Settings.DownloadDirectory = dialog.FolderName;
        AppServices.Settings.RaiseChanged();
        return Task.FromResult<string?>(dialog.FolderName);
    }

    internal async Task<object> GetExtensionsAsync()
    {
        var core = ActiveTab?.Core ?? Tabs.FirstOrDefault(t => t.Core is not null)?.Core;
        if (core is null) return Array.Empty<object>();
        if (!Settings.EnableExtensions)
            throw new InvalidOperationException("扩展程序支持已关闭。请先在设置中开启并重启浏览器。");

        var extensions = await core.Profile.GetBrowserExtensionsAsync();
        return extensions.Select(e => new
        {
            id = e.Id,
            name = e.Name,
            enabled = e.IsEnabled
        }).ToList();
    }

    internal async Task<object> LoadUnpackedExtensionAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择解压后的扩展程序文件夹",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) throw new InvalidOperationException("已取消选择。");

        var core = ActiveTab?.Core ?? Tabs.FirstOrDefault(t => t.Core is not null)?.Core
            ?? throw new InvalidOperationException("浏览器尚未准备好。");
        if (!Settings.EnableExtensions)
            throw new InvalidOperationException("扩展程序支持已关闭。请先在设置中开启并重启浏览器。");

        var extension = await core.Profile.AddBrowserExtensionAsync(dialog.FolderName);
        ShowToast($"已安装扩展：{extension.Name}");
        return new { id = extension.Id, name = extension.Name, enabled = extension.IsEnabled };
    }

    internal Task<object> ChooseNewTabBackgroundAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择新标签页背景图片",
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return Task.FromResult<object>(new { canceled = true });

        var info = new FileInfo(dialog.FileName);
        if (info.Length > 25 * 1024 * 1024)
            throw new InvalidOperationException("背景图片不能大于 25 MB。");

        var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif"))
            throw new InvalidOperationException("只支持 JPG、PNG、WebP、BMP 或 GIF 图片。");

        Directory.CreateDirectory(AppPaths.NewTabBackgrounds);
        var fileName = $"bg-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        var target = Path.Combine(AppPaths.NewTabBackgrounds, fileName);
        File.Copy(dialog.FileName, target, overwrite: false);

        DeleteNewTabBackgroundFile(Settings.NewTabBackground);
        Settings.NewTabBackground = $"{UrlHelper.BackgroundOrigin}/{Uri.EscapeDataString(fileName)}";
        AppServices.Settings.RaiseChanged();
        ShowToast("新标签页背景已更新");
        return Task.FromResult<object>(new { url = Settings.NewTabBackground });
    }

    internal Task<object> ClearNewTabBackgroundAsync()
    {
        DeleteNewTabBackgroundFile(Settings.NewTabBackground);
        Settings.NewTabBackground = "";
        AppServices.Settings.RaiseChanged();
        ShowToast("已恢复默认新标签页背景");
        return Task.FromResult<object>(new { cleared = true });
    }

    private static void DeleteNewTabBackgroundFile(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !url.StartsWith(UrlHelper.BackgroundOrigin + "/", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var name = Uri.UnescapeDataString(url[(UrlHelper.BackgroundOrigin.Length + 1)..]);
            name = Path.GetFileName(name);
            if (string.IsNullOrWhiteSpace(name)) return;
            var path = Path.Combine(AppPaths.NewTabBackgrounds, name);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Failed to delete old new-tab background: " + ex.Message);
        }
    }

    internal async Task<object> InstallCrxFromDialogAsync()
    {
        if (!Settings.EnableExtensions)
            throw new InvalidOperationException("扩展程序支持已关闭。请先在设置中开启并重启浏览器。");

        var dialog = new OpenFileDialog
        {
            Title = "选择 CRX 扩展包",
            Filter = "Chromium 扩展包 (*.crx)|*.crx|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return new { installed = false };

        var confirm = MessageBox.Show(this,
            "将从本地文件安装 Chromium 扩展。\n\n" +
            "扩展程序可以读取和修改你在浏览器中访问的内容，拥有与网页相同的敏感权限。\n" +
            "请只安装来自可信来源的 CRX，并确认你有权使用它。\n\n" +
            "本地 CRX 不会进行商店签名验证。继续安装吗？",
            "安装本地 CRX 扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
            return new { installed = false };

        var core = ActiveTab?.Core ?? Tabs.FirstOrDefault(t => t.Core is not null)?.Core
            ?? throw new InvalidOperationException("浏览器尚未准备好。");

        var extensionsRoot = Path.Combine(AppPaths.DataRoot, "Extensions");
        Directory.CreateDirectory(extensionsRoot);
        var package = await CrxInstaller.ExtractAsync(dialog.FileName, extensionsRoot);

        try
        {
            var extension = await core.Profile.AddBrowserExtensionAsync(package.ExtensionFolder);
            AppPaths.Log($"Installed CRX extension {extension.Name} {package.Version} from {dialog.FileName}");
            ShowToast($"已安装扩展：{extension.Name}");
            await RefreshExtensionsAsync();
            return new
            {
                installed = true,
                id = extension.Id,
                name = extension.Name,
                version = package.Version,
                manifestVersion = package.ManifestVersion
            };
        }
        catch
        {
            CrxInstaller.TryDelete(package.ExtensionFolder);
            throw;
        }
    }

    internal async Task<bool> SetExtensionEnabledAsync(string id, bool enabled)
    {
        var core = ActiveTab?.Core ?? Tabs.FirstOrDefault(t => t.Core is not null)?.Core;
        if (core is null) return false;
        var extensions = await core.Profile.GetBrowserExtensionsAsync();
        var extension = extensions.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        if (extension is null) return false;
        await extension.EnableAsync(enabled);
        ShowToast(enabled ? $"已启用扩展：{extension.Name}" : $"已停用扩展：{extension.Name}");
        return true;
    }

    internal async Task<bool> RemoveExtensionAsync(string id)
    {
        var core = ActiveTab?.Core ?? Tabs.FirstOrDefault(t => t.Core is not null)?.Core;
        if (core is null) return false;
        var extensions = await core.Profile.GetBrowserExtensionsAsync();
        var extension = extensions.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        if (extension is null) return false;
        await extension.RemoveAsync();
        ShowToast($"已移除扩展：{extension.Name}");
        return true;
    }

    internal object GetBrowserInfo()
    {
        var runtime = BrowserEnvironment.RuntimeVersion;
        var chromium = runtime.Split('.')[0];
        return new
        {
            appVersion = AppPaths.Version,
            webViewVersion = runtime,
            chromiumVersion = chromium,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            dataPath = AppPaths.DataRoot,
            isIncognito = _isIncognito,
            searchEngine = UrlHelper.GetSearchEngineName(Settings),
            profile = _isIncognito ? "无痕配置" : "本地个人资料"
        };
    }

    internal List<object> GetTopSites(int count)
        => AppServices.History.TopSites(count).Select(h => (object)new
        {
            h.Url,
            h.Title,
            h.Host,
            visits = h.VisitCount
        }).ToList();

    internal void ShowToastMessage(string message) => ShowToast(message);

    internal void ToggleFullScreenFromPage() => ToggleFullScreen();

    internal void OpenNewWindowFromPage(string? url, bool incognito = false) => OpenNewWindow(url, incognito);

    internal void ClearCurrentSiteData() => _ = ClearSiteDataAsync();
}
