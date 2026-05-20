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

# 复制 UTF-8 中文说明（带 BOM）；目标中文文件名由 notes-manifest.json 提供，避免 ps1 内嵌中文路径乱码
Get-ChildItem -Path $dist -Filter "*.txt" -ErrorAction SilentlyContinue | Remove-Item -Force
$notesDir = Join-Path $PSScriptRoot "extension-install-notes"
$manifestPath = Join-Path $notesDir "notes-manifest.json"
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$utf8Bom = New-Object System.Text.UTF8Encoding $true
$manifestJson = [System.IO.File]::ReadAllText($manifestPath, $utf8NoBom)
$manifest = $manifestJson | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $src = Join-Path $notesDir $entry.src
    if (-not (Test-Path $src)) { throw "Missing install note: $src" }
    $text = [System.IO.File]::ReadAllText($src, $utf8NoBom)
    $dest = Join-Path $dist $entry.dest
    [System.IO.File]::WriteAllText($dest, $text, $utf8Bom)
}

Write-Host "Extension zips written to: $dist"
