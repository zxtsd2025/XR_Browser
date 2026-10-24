[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath
Set-Location $root

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".packages"

Write-Host "==> Restoring NuGet packages" -ForegroundColor Cyan
dotnet restore "src\XRBrowser\XRBrowser.csproj" --configfile ".\NuGet.config" -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

$out = Join-Path $root "dist\XR Browser"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

Write-Host "==> Publishing XR Browser ($Configuration, win-x64)" -ForegroundColor Cyan
dotnet publish "src\XRBrowser\XRBrowser.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $out `
    --no-restore `
    -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Copy-Item "README.md" $out -Force
Copy-Item "LICENSE" $out -Force
if (Test-Path "docs") {
    Copy-Item "docs" $out -Recurse -Force
}

$exe = Join-Path $out "XR.exe"
if (-not (Test-Path $exe)) { throw "Publish succeeded but XR.exe was not found." }

if ($Zip) {
    $zipPath = Join-Path $root "dist\XR-Browser-1.0.0-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zipPath
    Write-Host "==> Archive: $zipPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done: $exe" -ForegroundColor Green
Write-Host "Requires the .NET 8 Desktop Runtime and the Microsoft Edge WebView2 Runtime." -ForegroundColor DarkGray
