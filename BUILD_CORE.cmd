@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "VERSION=0.1.2"
set "PROJECT=src\Alekrythae.Core\Alekrythae.Core.csproj"
set "PUBLISH_DIR=dist\Alekrythae-Core-v%VERSION%-Windows-x64"
set "RELEASE_DIR=release"
set "ZIP_PATH=%RELEASE_DIR%\Alekrythae-Core-v%VERSION%-Windows-x64.zip"
set "SHORT_BUILD_ROOT=%TEMP%\AlekCore-Release"
set "SHORT_OBJ=%SHORT_BUILD_ROOT%\obj"
set "SHORT_BIN=%SHORT_BUILD_ROOT%\bin"

set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "NUGET_XMLDOC_MODE=skip"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [HATA] .NET 10 SDK bulunamadi.
  echo Bu komut gelistirici icindir. Son kullanici yayin paketini indirmelidir.
  pause
  exit /b 1
)

if exist "dist" rmdir /s /q "dist"
if exist "%RELEASE_DIR%" rmdir /s /q "%RELEASE_DIR%"
if exist "%SHORT_BUILD_ROOT%" rmdir /s /q "%SHORT_BUILD_ROOT%"
for /d /r "src" %%D in (bin,obj) do @if exist "%%D" rmdir /s /q "%%D"
mkdir "%RELEASE_DIR%"
mkdir "%SHORT_OBJ%"
mkdir "%SHORT_BIN%"

echo [1/4] NuGet paketleri guvenli surumlerle hazirlaniyor...
dotnet restore "%PROJECT%" -r win-x64 -p:SelfContained=true -p:BaseIntermediateOutputPath="%SHORT_OBJ%/" -p:MSBuildProjectExtensionsPath="%SHORT_OBJ%/" -p:BaseOutputPath="%SHORT_BIN%/"
if errorlevel 1 goto :fail

echo [2/4] Temiz Windows x64 paketi uretiliyor...
dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  --no-restore ^
  -p:UseAppHost=true ^
  -p:GenerateRuntimeConfigurationFiles=true ^
  -p:GenerateDependencyFile=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -p:BaseIntermediateOutputPath="%SHORT_OBJ%/" ^
  -p:MSBuildProjectExtensionsPath="%SHORT_OBJ%/" ^
  -p:BaseOutputPath="%SHORT_BIN%/" ^
  -o "%PUBLISH_DIR%"
if errorlevel 1 goto :fail

if not exist "%PUBLISH_DIR%\Alekrythae Core.exe" goto :fail
if not exist "%PUBLISH_DIR%\Alekrythae Core.runtimeconfig.json" goto :fail
if not exist "%PUBLISH_DIR%\Alekrythae Core.deps.json" goto :fail

echo [3/4] Release ZIP dosyasi hazirlaniyor...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_PATH%' -CompressionLevel Optimal -Force"
if errorlevel 1 goto :fail

echo [4/4] Gecici bin ve obj klasorleri temizleniyor...
for /d /r "src" %%D in (bin,obj) do @if exist "%%D" rmdir /s /q "%%D"
if exist "%SHORT_BUILD_ROOT%" rmdir /s /q "%SHORT_BUILD_ROOT%"

echo.
echo [TAMAM] GitHub Release dosyasi hazir:
echo %ZIP_PATH%
explorer "%RELEASE_DIR%"
pause
exit /b 0

:fail
if exist "%SHORT_BUILD_ROOT%" rmdir /s /q "%SHORT_BUILD_ROOT%"
echo.
echo [HATA] Release paketi olusturulamadi.
pause
exit /b 1
