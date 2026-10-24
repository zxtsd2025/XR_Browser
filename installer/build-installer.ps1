[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$SkipPrereqDownload
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".packages"

$installer = Join-Path $root "installer"
$payload = Join-Path $installer "payload"
$prereq = Join-Path $installer "prereq\MicrosoftEdgeWebView2Setup.exe"
$iss = Join-Path $installer "installer.iss"

if (-not $SkipPublish) {
    Write-Host "==> Publishing self-contained payload (win-x64)" -ForegroundColor Cyan
    if (Test-Path $payload) { Remove-Item -Recurse -Force $payload }
    New-Item -ItemType Directory -Force -Path $payload | Out-Null

    dotnet restore "src\XRBrowser\XRBrowser.csproj" --configfile ".\NuGet.config" -p:SelfContained=true -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    dotnet publish "src\XRBrowser\XRBrowser.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $payload `
        --no-restore `
        -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    Copy-Item "README.md" $payload -Force
    Copy-Item "LICENSE" $payload -Force
}

if (-not (Test-Path (Join-Path $payload "XR.exe"))) {
    throw "Payload does not contain XR.exe: $payload"
}

if (-not (Test-Path $prereq) -and -not $SkipPrereqDownload) {
    Write-Host "==> Downloading WebView2 bootstrapper" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path (Split-Path $prereq -Parent) | Out-Null
    Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/p/?LinkId=2124703" `
        -OutFile $prereq -UseBasicParsing
}
if (-not (Test-Path $prereq)) { throw "WebView2 bootstrapper is missing: $prereq" }

# Inno Setup's Unicode language file must be UTF-8 with BOM on older compilers.
$lang = Join-Path $installer "Languages\ChineseSimplified.isl"
if (Test-Path $lang) {
    $bytes = [IO.File]::ReadAllBytes($lang)
    if (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
        $bom = [byte[]](0xEF, 0xBB, 0xBF)
        [IO.File]::WriteAllBytes($lang, [byte[]]($bom + $bytes))
    }
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup 6 first: winget install JRSoftware.InnoSetup"
}

$output = Join-Path $installer "output"
if (Test-Path $output) { Remove-Item -Recurse -Force $output }

Write-Host "==> Compiling installer: $iscc" -ForegroundColor Cyan
Push-Location $installer
try {
    & $iscc $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
}
finally {
    Pop-Location
}

$setup = Join-Path $output "XR-Browser-Setup-1.0.0.exe"
if (-not (Test-Path $setup)) { throw "Installer output not found: $setup" }
Write-Host ""
Write-Host "Done: $setup" -ForegroundColor Green
Get-Item $setup | Select-Object FullName, Length, LastWriteTime
