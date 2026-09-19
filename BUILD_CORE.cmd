@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "VERSION=2.0.0"
set "PROJECT=src\Alekrythae.Core\Alekrythae.Core.csproj"
set "RELEASE_DIR=release\Alekrythae-Core-v%VERSION%-Windows-x64"
set "SHORT_BUILD_ROOT=%TEMP%\AlekCore-Release"
set "SHORT_OBJ=%SHORT_BUILD_ROOT%\obj"
set "SHORT_BIN=%SHORT_BUILD_ROOT%\bin"

set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "NUGET_XMLDOC_MODE=skip"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [HATA] .NET 10 SDK bulunamadi.
  pause
  exit /b 1
)

if exist "%RELEASE_DIR%" rmdir /s /q "%RELEASE_DIR%"
if exist "%SHORT_BUILD_ROOT%" rmdir /s /q "%SHORT_BUILD_ROOT%"
for /d /r "src" %%D in (bin,obj) do @if exist "%%D" rmdir /s /q "%%D"
mkdir "%RELEASE_DIR%"
mkdir "%SHORT_OBJ%"
mkdir "%SHORT_BIN%"

echo [1/3] NuGet paketleri hazirlaniyor...
dotnet restore "%PROJECT%" -r win-x64 -p:SelfContained=true -p:BaseIntermediateOutputPath="%SHORT_OBJ%/" -p:MSBuildProjectExtensionsPath="%SHORT_OBJ%/" -p:BaseOutputPath="%SHORT_BIN%/"
if errorlevel 1 goto :fail

echo [2/3] Windows x64 Release klasoru uretiliyor...
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
  -o "%RELEASE_DIR%"
if errorlevel 1 goto :fail

if not exist "%RELEASE_DIR%\Alekrythae Core.exe" goto :fail
if not exist "%RELEASE_DIR%\Alekrythae Core.runtimeconfig.json" goto :fail
if not exist "%RELEASE_DIR%\Alekrythae Core.deps.json" goto :fail

echo [3/3] Gecici bin ve obj klasorleri temizleniyor...
for /d /r "src" %%D in (bin,obj) do @if exist "%%D" rmdir /s /q "%%D"
if exist "%SHORT_BUILD_ROOT%" rmdir /s /q "%SHORT_BUILD_ROOT%"

echo.
echo [TAMAM] Release klasoru hazir:
echo %RELEASE_DIR%
explorer "%RELEASE_DIR%"
pause
exit /b 0

:fail
if exist "%SHORT_BUILD_ROOT%" rmdir /s /q "%SHORT_BUILD_ROOT%"
echo.
echo [HATA] Release build olusturulamadi.
pause
exit /b 1
