@echo off
setlocal
set "ALEK_TMP_PS=%TEMP%\Alekrythae_Uninstall_%RANDOM%_%RANDOM%.ps1"
copy /Y "%~dp0KALDIR_ALEKRYTHAE_CORE.ps1" "%ALEK_TMP_PS%" >nul
if errorlevel 1 (
  echo Kaldirici gecici klasore kopyalanamadi.
  pause
  exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%ALEK_TMP_PS%" -InstallRoot "%~dp0" -Mode Ask
endlocal
