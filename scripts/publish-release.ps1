#Requires -Version 5.0
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$out = Join-Path $root "dist\KeyNest-Windows-Release"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

Write-Host "==> 发布到: $out"
Write-Host "==> 需已安装 .NET 8 SDK（建议在 Windows 上执行）"

dotnet publish "src\KeyNestForWin\KeyNestForWin.csproj" `
    -c Release `
    -o $out `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true

Write-Host ""
Write-Host "完成。主程序: $(Join-Path $out 'KeyNestForWin.exe')"
Write-Host "可将 dist\KeyNest-Windows-Release 整夹打成 zip 分发（Win10/11 x64）。"
