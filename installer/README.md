# XR Browser 安装程序

## 直接安装

双击：

`dist\XR-Browser-Setup-1.0.0.exe`

然后按向导下一步即可。

安装器是普通的 Inno Setup 图形安装程序，不需要命令行参数。

## 安装内容

- 默认安装到当前用户目录：
  `%LOCALAPPDATA%\Programs\XR Browser`
- 不需要管理员权限；
- 创建开始菜单快捷方式；
- 可选创建桌面快捷方式；
- 自带 .NET 8 运行时，不依赖系统安装的 .NET；
- 如果缺少 WebView2 Runtime，会先运行随安装包附带的 WebView2 引导安装程序；
- 可选把 XR Browser 注册到 Windows 的“默认应用”列表；
- 安装完成后可以立即启动浏览器。

## 重新生成安装程序

在仓库根目录运行：

```powershell
.\installer\build-installer.ps1
```

脚本会：

1. 发布自包含的 win-x64 版本；
2. 准备 WebView2 引导安装程序；
3. 调用 Inno Setup 6 编译安装包。

输出：

`installer\output\XR-Browser-Setup-1.0.0.exe`

## 卸载

在 Windows“设置 → 应用”中卸载 XR Browser，或运行安装目录里的卸载程序。

卸载不会删除 `%LOCALAPPDATA%\XRBrowser` 下的浏览数据、书签、历史记录和设置。

## 签名说明

当前安装包没有代码签名证书。Windows SmartScreen 可能会提示“未知发布者”，这是未签名程序的正常提示，不影响安装。
