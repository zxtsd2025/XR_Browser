#define MyAppName "XR Browser"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "XR Browser"
#define MyAppExeName "XR.exe"
#define MyAppId "{{4E76B0B6-7C2F-4C82-9B31-6A5B5A5B2C21}"
#define WebView2Client "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\XR Browser
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=XR-Browser-Setup-{#MyAppVersion}
SetupIconFile=..\src\XRBrowser\Assets\XR.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ChangesAssociations=yes
LicenseFile=..\LICENSE
UsePreviousAppDir=yes
VersionInfoVersion=1.0.0.0
VersionInfoDescription=XR Browser Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："
Name: "registerbrowser"; Description: "将 XR Browser 注册为可选浏览器"; GroupDescription: "浏览器注册："; Flags: checkedonce

[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "prereq\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\XR Browser"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\卸载 XR Browser"; Filename: "{uninstallexe}"
Name: "{autodesktop}\XR Browser"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Start-menu internet client registration (makes XR appear in Default Apps).
Root: HKCU; Subkey: "Software\Clients\StartMenuInternet\XR Browser"; ValueType: string; ValueName: ""; ValueData: "XR Browser"; Flags: uninsdeletekey; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\Clients\StartMenuInternet\XR Browser\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\Clients\StartMenuInternet\XR Browser\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: registerbrowser

; Browser capabilities.
Root: HKCU; Subkey: "Software\XR Browser\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "XR Browser"; Flags: uninsdeletekey; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\XR Browser\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "XR Browser - Chromium-based web browser"; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\XR Browser\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\XR Browser\Capabilities\URLAssociations"; ValueType: string; ValueName: "http"; ValueData: "XR Browser.html"; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\XR Browser\Capabilities\URLAssociations"; ValueType: string; ValueName: "https"; ValueData: "XR Browser.html"; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\XR Browser\Capabilities\FileAssociations"; ValueType: string; ValueName: ".html"; ValueData: "XR Browser.html"; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\XR Browser\Capabilities\FileAssociations"; ValueType: string; ValueName: ".htm"; ValueData: "XR Browser.html"; Tasks: registerbrowser

; ProgId used by URL/file associations.
Root: HKCU; Subkey: "Software\Classes\XR Browser.html"; ValueType: string; ValueName: ""; ValueData: "XR Browser Document"; Flags: uninsdeletekey; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\Classes\XR Browser.html\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: registerbrowser
Root: HKCU; Subkey: "Software\Classes\XR Browser.html\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: registerbrowser

; Register in the Windows "Default apps" list.
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "XR Browser"; ValueData: "Software\XR Browser\Capabilities"; Flags: uninsdeletevalue; Tasks: registerbrowser

; App Paths for Run dialog / shell.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "正在安装 Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated; Check: NeedsWebView2
Filename: "{app}\{#MyAppExeName}"; Description: "启动 XR Browser"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function IsWebView2Installed: Boolean;
begin
  Result :=
    RegKeyExists(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{#WebView2Client}') or
    RegKeyExists(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{#WebView2Client}') or
    RegKeyExists(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{#WebView2Client}');
end;

function NeedsWebView2: Boolean;
begin
  Result := not IsWebView2Installed;
end;
