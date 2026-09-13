@echo off
setlocal EnableExtensions
cd /d "%~dp0"

findstr /C:"private WebView2 _webView;" "src\Alekrythae.Core\CosmicGate.cs" >nul
if errorlevel 1 (
  echo [HATA] Standart WebView2 performans degisikligi bulunamadi.
  pause
  exit /b 1
)

findstr /C:"MemoryUsageTargetLevel" "src\Alekrythae.Core\CosmicGate.cs" >nul
if errorlevel 1 (
  echo [HATA] WebView2 guc yonetimi performans degisikligi bulunamadi.
  pause
  exit /b 1
)

echo [OK] Performans patch kaynaklari dogrulandi.
call BUILD_CORE.cmd
