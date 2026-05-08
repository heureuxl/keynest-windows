@echo off
setlocal
cd /d "%~dp0.."
set "OUT=%cd%\dist\KeyNest-Windows-Release"
if not exist "dist" mkdir "dist"
echo ==^> 发布到: %OUT%
echo ==^> 需已安装 .NET 8 SDK，且建议在本机 Windows 上执行（WPF 目标）
dotnet publish "src\KeyNestForWin\KeyNestForWin.csproj" ^
  -c Release ^
  -o "%OUT%" ^
  -r win-x64 ^
  --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:EnableCompressionInSingleFile=true
if errorlevel 1 (
  echo 发布失败。
  exit /b 1
)
echo.
echo 完成。主程序: %OUT%\KeyNestForWin.exe
echo 可将整个文件夹 KeyNest-Windows-Release 打包为 zip 分发给其他电脑（Win10/11 x64）。
endlocal
