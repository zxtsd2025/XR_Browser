# XR Browser — Source

XR Browser is a fast, Chromium-based web browser for Windows with a clean Chrome-style interface.

This directory contains the WPF application source. The project is Windows-only and uses C#, .NET 8, WPF, and Microsoft Edge WebView2.

## Requirements

- Windows 10/11 x64
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime

## Build and run

From the repository root:

```powershell
dotnet restore src/XRBrowser/XRBrowser.csproj
dotnet build src/XRBrowser/XRBrowser.csproj -c Debug -r win-x64
dotnet run --project src/XRBrowser/XRBrowser.csproj
```

## Publish

```powershell
dotnet publish src/XRBrowser/XRBrowser.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o "dist/XR Browser"
```

## Source layout

```text
src/
└─ XRBrowser/
   ├─ App.xaml / App.xaml.cs        Application startup and services
   ├─ MainWindow.xaml               Browser window, tabs, menus, styles
   ├─ MainWindow.xaml.cs            Tabs, chrome, animations, settings
   ├─ MainWindow.Web.cs             WebView2 lifecycle, navigation, downloads
   ├─ MainWindow.Api.cs             Host API used by internal HTML pages
   ├─ Models/                       BrowserTab, settings, bookmarks, history
   ├─ Services/                     Persistence, AdGuard, Dark Reader, CRX,
   │                                sessions, theme, native interop
   ├─ Dialogs/                      Bookmark and permission dialogs
   ├─ Themes/                       Light/dark resource dictionaries and styles
   └─ Assets/
      ├─ Internal/                  Built-in HTML pages and JavaScript
      └─ DarkReader/                Bundled Dark Reader script
```

## Architecture

XR Browser uses a WPF shell and a WebView2 Chromium renderer:

- The WPF layer owns the custom window chrome, tab strip, toolbar, menus, dialogs, and settings.
- Each `BrowserTab` owns a WebView2 control.
- Navigation, downloads, permissions, find-in-page, and extension handling live in `MainWindow.Web.cs`.
- Persistent data and browser services are registered through `AppServices`.
- Built-in pages (`xr://settings`, `xr://newtab`, etc.) are local HTML pages hosted by WebView2 and communicate with the host through a small bridge.

## Features

- Chrome-style tabs, tab reordering, and tab tear-off into a new window
- Address bar search, bookmarks, history, downloads, and session restore
- Find-in-page, zoom, fullscreen, print, and developer tools
- Ad blocking with AdGuard-style filter lists
- Dark Reader integration
- Local CRX and unpacked Chromium extension installation
- Light/dark themes and configurable UI animations
- Frosted-glass menus and a sliding active-tab background

## Limitations

- Windows 10/11 x64 only.
- WebView2 must be available on the system.
- Extension support depends on WebView2’s capabilities.
- The AdGuard engine is a lightweight implementation of common network and cosmetic rules, not the full AdGuard extension.
- The browser UI and web content are separate rendering layers; some advanced effects are implemented as compatibility fallbacks.

## Related documentation

- [Project README](../README.md)
- [Installer guide](../installer/README.md)
