# XR Browser

**简体中文** | [English](README.en.md)

一个使用 **C# / .NET 8 / WPF / Chromium（Microsoft Edge WebView2）** 构建的 Windows 桌面浏览器。界面按照 Chrome 的信息层级与交互习惯设计，同时保留 Windows 原生窗口的拖拽、缩放、贴靠和 DPI 支持。

## 安装

直接双击安装包：

[XR-Browser-Setup-1.0.0.exe](dist/XR-Browser-Setup-1.0.0.exe)

安装器是普通的图形向导，默认安装到当前用户目录，不需要命令行参数。

## 功能

### 浏览体验
- Chromium 内核（WebView2 Evergreen Runtime），支持现代网页、PDF 预览和 Chrome DevTools。
- 多标签页：新建、关闭、恢复关闭的标签、静音、复制、拖拽排序、标签溢出滚动。
- Chrome 风格顶部标签条、工具栏、可折叠书签栏、浮动地址栏建议和下载弹层。
- 地址栏同时支持网址与搜索：默认 Bing，自动补全本地历史/书签，支持 Google、Bing、百度、DuckDuckGo、Yandex 和自定义搜索模板。
- URL 输入纠错：自动处理全角句号/冒号/斜杠，以及 `https:\\`、`https//` 这类反斜杠或漏写协议斜杠的情况。
- 极简新标签页：默认只保留搜索框；右上角设置按钮可开启“最近访问”，或选择本地图片作为自定义背景。
- 主页、后退/前进、刷新/停止、页面缩放、打印、在页面内查找、全屏。
- 会话恢复：正常退出后重新打开，恢复标签页与窗口位置。
- 多窗口与无痕窗口：无痕窗口使用 WebView2 的 off-the-record profile，不写入历史。

### 数据与管理
- 书签：星标添加、书签栏、内置书签管理页、右键打开/编辑/删除。
- 历史记录：按日期分组、搜索、单条删除、批量清除；无痕窗口不记录历史。
- 下载管理：下载进度、暂停/继续/取消、打开文件、在文件夹中显示、下载弹层与下载页。
- 权限提示：摄像头、麦克风、定位、通知、剪贴板、自动播放等。
- 浏览数据清理：历史、Cookie、缓存、密码、自动填充、下载记录；支持清除当前网站数据。
- AdGuard 广告过滤：使用 AdGuard Base 与中文过滤列表拦截广告/跟踪请求，并隐藏常见广告元素；主菜单、网页右键菜单和设置页均可开关。
- Dark Reader：内置 Dark Reader 4.9.133，为网页启用深色主题；不改变浏览器自身界面，主菜单、网页右键菜单和设置页均可开关。
- 界面动画：标签页与弹出菜单使用可中断的阻尼弹簧（低延迟、轻微回弹、连续续接）并带速度驱动的动态模糊；切换标签时，活动标签的 accent 背景会作为一块共享滑块跟随当前标签移动，带轻微拉伸回弹。菜单按钮可再次点击收起，菜单会向触发按钮方向缩放退场。可在设置 → 外观中关闭或调节速率。
- 标签拖出：把标签拖到标签栏外会创建一个独立窗口，并在光标位置附近打开；新窗口按当前 URL 重新载入，不迁移原标签的 WebView 历史与表单状态。
- 标题栏：保持不透明，避免透明标题栏在不同 DWM/壁纸环境下出现发黑或材质失效。
- 磨砂弹层：弹出菜单和右键菜单使用打开前的屏幕快照做双通道高斯模糊，叠加轻微高光，并可调节材质透明度。
- 右键菜单：网页右键菜单改为 WPF 自绘菜单，支持打开/关闭动画和截屏模糊背景，不再使用 Chromium 原生菜单。
- 扩展程序：工具栏拼图菜单可快速查看、启用/停用扩展；支持从本地 CRX（CRX2/CRX3）安装，或加载解压后的 Chromium 扩展；设置页可移除扩展（受 WebView2 扩展支持范围限制）。
- 设置持久化到本地 JSON；支持系统/浅色/深色主题。

### 内置页面
地址栏可访问：

| 地址 | 内容 |
| --- | --- |
| `xr://newtab` | 新标签页（搜索、常用网站） |
| `xr://settings` | 设置 |
| `xr://history` | 历史记录 |
| `xr://bookmarks` | 书签管理 |
| `xr://downloads` | 下载内容 |
| `xr://extensions` | 扩展程序 |
| `xr://about` | 版本、内核和功能概览 |

## 界面截图

| 新标签页 | 带背景的新标签页 |
| --- | --- |
| ![XR Browser 极简新标签页](docs/new-tab.png) | ![带最近访问和自定义背景的新标签页](docs/new-tab-background.png) |

| 新标签页设置 | 设置页面 |
| --- | --- |
| ![新标签页右上角设置面板](docs/new-tab-settings.png) | ![设置页面](docs/settings-light.png) |

| 历史记录 | 主菜单 |
| --- | --- |
| ![历史记录](docs/history.png) | ![主菜单](docs/menu.png) |

| 页面内查找 | 网页右键菜单 |
| --- | --- |
| ![页面内查找](docs/find-in-page.png) | ![网页右键菜单中的 AdGuard 与 Dark Reader 开关](docs/context-menu.png) |

| 扩展程序 |
| --- |
| ![工具栏扩展程序菜单](docs/extensions.png) |

> 菜单磨砂背景使用打开前的屏幕快照做高斯模糊；它是静态背景，页面在菜单打开期间继续播放动画时，模糊层不会实时刷新。

## 快速运行

已经构建好的版本位于：

```text
dist\XR Browser\XR.exe
```

直接双击 `XR.exe` 即可。首次运行需要：

1. Windows 10 1809+ 或 Windows 11（x64）。
2. [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。
3. [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Windows 10/11 通常已随 Edge 安装）。

> 可执行文件名为 `XR.exe`。部分游戏覆盖层会按进程名注入 DLL，使用 `XR.exe` 可以避开这类误注入导致的宿主崩溃。

## 从源码构建

```powershell
# 第一次需要联网还原 WebView2 SDK
dotnet restore src\XRBrowser\XRBrowser.csproj --configfile .\NuGet.config

dotnet build src\XRBrowser\XRBrowser.csproj -c Release -r win-x64
# 输出：src\XRBrowser\bin\Release\net8.0-windows\win-x64\XR.exe
```

或使用发布脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
# 生成 dist\XR Browser\XR.exe
# 可选：powershell -ExecutionPolicy Bypass -File .\build.ps1 -Zip
#       生成 dist\XR-Browser-1.0.0-win-x64.zip
```

项目文件：`XRBrowser.sln`（项目名仍为 `XRBrowser`，程序集/可执行文件名为 `XR`）。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Ctrl+T` / `Ctrl+W` | 新建标签页 / 关闭当前标签页 |
| `Ctrl+Shift+T` | 恢复最近关闭的标签页 |
| `Ctrl+N` / `Ctrl+Shift+N` | 新建窗口 / 新建无痕窗口 |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | 切换下一个 / 上一个标签页 |
| `Ctrl+1` … `Ctrl+8` / `Ctrl+9` | 跳到第 N 个 / 最后一个标签页 |
| `Ctrl+L` / `Alt+D` | 聚焦地址栏 |
| `Ctrl+D` | 添加/移除当前页书签 |
| `Ctrl+H` / `Ctrl+J` | 历史记录 / 下载内容 |
| `Ctrl+Shift+O` | 书签管理 |
| `Ctrl+F` | 页面内查找（`Enter` 下一个，`Shift+Enter` 上一个） |
| `Ctrl+P` | 打印 |
| `Ctrl+R` / `F5` | 重新加载 |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` | 放大 / 缩小 / 重置缩放 |
| `F11` | 全屏（`Esc` 退出） |
| `F12` / `Ctrl+Shift+I` | 开发者工具 |
| `Alt+←` / `Alt+→` / `Alt+Home` | 后退 / 前进 / 主页 |
| `Ctrl+Shift+B` | 显示/隐藏书签栏 |

## 数据目录

默认位置：

```text
%LOCALAPPDATA%\XRBrowser\
├─ settings.json
├─ bookmarks.json
├─ history.json
├─ session.json
├─ Favicons\            # 书签栏站点图标缓存（PNG）
├─ Filters\             # AdGuard Base / 中文过滤列表缓存
├─ NewTabBackgrounds\   # 新标签页自定义背景图片
├─ WebView2\            # Chromium profile / cache / cookies
└─ Logs\xr-browser.log
```

- 可执行文件旁放置 `portable.txt` 后，数据会保存到程序目录的 `Data\`。
- 也可以通过环境变量 `XR_BROWSER_DATA_DIR` 指定数据根目录。
- 下载默认保存到 `%USERPROFILE%\Downloads\XR Browser`，可在设置中更改。

## 项目结构

```text
src/XRBrowser/
├─ MainWindow.xaml(.cs)        # Chrome 风格窗口、标签条、工具栏、菜单
├─ MainWindow.Web.cs           # 标签/WebView2 生命周期、导航、下载、权限、查找
├─ MainWindow.Api.cs           # 内部页面调用的宿主能力
├─ Models/                     # Tab、书签、历史、下载、设置
├─ Services/                   # 持久化、主题、URL、WebView2、内部页面桥
├─ Dialogs/                    # 权限、书签编辑对话框
├─ Themes/                     # 浅色/深色资源与控件样式
└─ Assets/Internal/            # 新标签页、设置/历史/书签/下载等 HTML 应用
```

内部页面通过 `window.chrome.webview.postMessage` 与 `InternalPageBridge` 通信，不依赖外部 CDN，可完全离线打开。

书签栏图标优先读取 Chromium 已下载的 favicon；首次加载时直接向站点请求 `/favicon.ico` 或页面中声明的 `<link rel="icon">`，不使用第三方 favicon 服务。

## 权限与沙箱说明

**WebView2 需要完整的桌面权限和可写的用户数据目录。** 在限制较严的文件沙箱中，Chromium 控制器可能创建失败（`E_UNEXPECTED`），或下载时弹出系统“另存为”窗口却无法写入文件。

- 如果运行时出现“浏览器引擎启动失败”，请把程序放到普通用户桌面环境中运行，确保 `%LOCALAPPDATA%` 与下载目录可写。
- 下载失败时，先在设置中把下载位置改到一个有写权限的目录（例如“下载”或用户目录下的文件夹）。
- 无痕模式仍然是 WebView2 的进程内私有 profile；它不会写入历史与会话，但不会改变操作系统的文件权限。
- 本项目的自动化测试在完整桌面权限下验证了导航、标签、查找、历史、书签、设置、主题、无痕窗口和会话恢复。

## 已知限制

- 不包含账号登录、云同步、密码库加密存储或跨设备同步。
- 下载列表只记录当前运行会话；Chromium 自身的下载历史可在设置中清理。
- 扩展支持取决于 WebView2 的扩展 API，不等同于 Chrome 网上应用店的完整能力。
- 广告过滤是轻量级 AdGuard 过滤列表引擎，只实现常见的网络规则和基础 cosmetic 规则；不执行 scriptlet、redirect、CSP 等 AdGuard 扩展动作，不能等同于 AdGuard 完整客户端。
- Dark Reader 通过注入网页脚本实现，部分强 CSP 页面、PDF、特殊 Web 应用可能不生效或出现配色异常；页面加载后应用，可能有短暂闪白。
- 新标签页背景支持 JPG、PNG、WebP、BMP、GIF，单张最大 25 MB；图片保存在本地数据目录，不参与同步。
- 受 WPF + WebView2（WebView2 为独立 HWND）的分层限制，无法实现 iOS Core Animation 那种真正跟手的拖拽转场和方向性 motion blur；当前为可中断阻尼弹簧 + 速度驱动的全向高斯模糊。
- 标题栏保持不透明；不再使用 DWM Mica/Acrylic 或截屏 chrome 背景，避免透明标题栏在纯色壁纸下看起来像主题色。
- 菜单模糊是打开前的截屏快照，双通道高斯模糊会增加右键菜单打开时的准备时间；这是 WPF `Popup` 分层窗口的限制。
- 默认浏览器按钮会打开 Windows 的“默认应用”设置页；Windows 不允许应用静默修改默认浏览器。
- 从 CRX 安装只做本地解包和安装，不验证 Microsoft/Chrome 商店签名，不提供商店级自动更新；扩展能否运行取决于 WebView2 对其 API 的支持程度。
- 第三方扩展商店没有统一的公开安装 API；XR Browser 不内置商店抓取，用户需自行获得有权使用的 CRX 文件。

## 第三方组件

- [Microsoft Edge WebView2 SDK](https://learn.microsoft.com/microsoft-edge/webview2/)（NuGet：`Microsoft.Web.WebView2`）
- Microsoft Edge WebView2 Runtime（需单独安装）
- [AdGuard 过滤列表](https://github.com/AdguardTeam/AdguardFilters)（运行时从 `filters.adtidy.org` 下载，不随程序打包；过滤列表版权归 AdGuard 所有）
- [Dark Reader](https://github.com/darkreader/darkreader) v4.9.133（MIT License，构建文件位于 `Assets/DarkReader/`）

源代码采用 MIT License，详见 [LICENSE](LICENSE)。
