@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if errorlevel 1 (
  echo [ERROR] Cannot open the Alekrythae Core source folder.
  exit /b 1
)
echo [INFO] Checking .NET 10 SDK and WebView2 Runtime, testing and building Core...
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0tools\Build-Core.ps1" %*
set "result=%errorlevel%"
if not "%result%"=="0" (
  echo.
  echo [ERROR] Build failed. Read the error above.
  if not defined CI pause
)
exit /b %result%
