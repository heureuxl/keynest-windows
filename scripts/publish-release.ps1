#Requires -Version 5.0
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$out = Join-Path $root "dist\KeyNest-Windows-Release"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$version = (Select-String -Path "src\KeyNestForWin\KeyNestForWin.csproj" -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
Write-Host "==> KeyNest for Windows v$version"
Write-Host "==> Output: $out"

dotnet publish "src\KeyNestForWin\KeyNestForWin.csproj" `
    -c Release `
    -o $out `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true

& "$PSScriptRoot\build-browser-extensions.ps1"

$bundleZip = Join-Path $root "dist\KeyNest-Windows-$version-win-x64.zip"
if (Test-Path $bundleZip) { Remove-Item $bundleZip -Force }

$bundleStage = Join-Path $root "dist\_bundle_stage"
if (Test-Path $bundleStage) { Remove-Item $bundleStage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $bundleStage | Out-Null

Copy-Item -Path $out -Destination (Join-Path $bundleStage "KeyNest-Windows-Release") -Recurse
Copy-Item -Path (Join-Path $root "dist\KeyNest-Chrome.zip") -Destination $bundleStage
Copy-Item -Path (Join-Path $root "dist\KeyNest-Edge.zip") -Destination $bundleStage
Get-ChildItem (Join-Path $root "dist\*.txt") | Copy-Item -Destination $bundleStage

Compress-Archive -Path (Join-Path $bundleStage "*") -DestinationPath $bundleZip -CompressionLevel Optimal
Remove-Item $bundleStage -Recurse -Force

Write-Host ""
Write-Host "Done."
Write-Host "  App:    $(Join-Path $out 'KeyNestForWin.exe')"
Write-Host "  Bundle: $bundleZip"
