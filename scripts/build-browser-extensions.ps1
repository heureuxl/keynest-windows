#Requires -Version 5.0
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

function Zip-ExtensionFolder {
    param([string]$SrcDir, [string]$ZipPath, [string]$InnerName)
    if (-not (Test-Path $SrcDir)) {
        throw "Extension folder not found: $SrcDir"
    }
    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("keynest-ext-" + [guid]::NewGuid().ToString("n"))
    $inner = Join-Path $stage $InnerName
    New-Item -ItemType Directory -Force -Path $inner | Out-Null
    Copy-Item -Path (Join-Path $SrcDir "*") -Destination $inner -Recurse -Force
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path $inner -DestinationPath $ZipPath -CompressionLevel Optimal
    Remove-Item $stage -Recurse -Force
}

$chrome = Join-Path $root "browser\chrome-extension"
$edge = Join-Path $root "browser\edge-extension"

Write-Host "==> KeyNest-Chrome.zip"
Zip-ExtensionFolder $chrome (Join-Path $dist "KeyNest-Chrome.zip") "KeyNest"

Write-Host "==> KeyNest-Edge.zip"
Zip-ExtensionFolder $edge (Join-Path $dist "KeyNest-Edge.zip") "KeyNest"

$chromeNote = @"
[KeyNest-Chrome.zip]
1. Extract the zip to get a folder named KeyNest (do not copy loose files only).
2. Open chrome://extensions, enable Developer mode, Load unpacked, select the KeyNest folder.
3. Unlock KeyNest desktop app and enable the localhost bridge (port 17373).
"@
$chromeNote | Out-File -FilePath (Join-Path $dist "Chrome扩展安装说明.txt") -Encoding utf8

$edgeNote = @"
[KeyNest-Edge.zip]
1. Extract the zip to get a folder named KeyNest.
2. Open edge://extensions, enable Developer mode, Load unpacked, select the KeyNest folder.
3. Unlock KeyNest desktop app and enable the localhost bridge (port 17373).
"@
$edgeNote | Out-File -FilePath (Join-Path $dist "Edge扩展安装说明.txt") -Encoding utf8

Write-Host "Extension zips written to: $dist"
