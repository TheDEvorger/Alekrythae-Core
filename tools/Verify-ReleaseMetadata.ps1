#requires -Version 5.1
<#
 Verifies that this source tree does not accidentally advertise another build version.
 This is an identity consistency gate, not proof that the program starts successfully.
#>
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = [IO.File]::ReadAllText((Join-Path $root 'VERSION')).Trim()
$project = [xml]([IO.File]::ReadAllText((Join-Path $root 'src/Alekrythae.Core/Alekrythae.Core.csproj')))
$projectVersion = [string]$project.Project.PropertyGroup.Version
$infoVersion = [string]$project.Project.PropertyGroup.InformationalVersion
$assemblyVersion = [string]$project.Project.PropertyGroup.AssemblyVersion
$fileVersion = [string]$project.Project.PropertyGroup.FileVersion
$programSource = [IO.File]::ReadAllText((Join-Path $root 'src/Alekrythae.Core/Program.cs'))
$buildScript = [IO.File]::ReadAllText((Join-Path $root 'tools/Build-Core.ps1'))
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid VERSION value: '$version'" }
if ($version -ne $projectVersion) { throw "VERSION '$version' differs from project Version '$projectVersion'" }
if ($assemblyVersion -ne ($version + '.0')) { throw "AssemblyVersion '$assemblyVersion' differs from VERSION '$version'" }
if ($fileVersion -ne ($version + '.0')) { throw "FileVersion '$fileVersion' differs from VERSION '$version'" }
if (-not $programSource.Contains(('DisplayVersion = "' + $version + '";'))) {
    throw 'Program.cs DisplayVersion differs from VERSION.'
}
if (-not $programSource.Contains(('Core v' + $version + ''))) {
    throw 'Program.cs DisplayName differs from VERSION.'
}
if ($infoVersion -ne $version) {
    throw "InformationalVersion '$infoVersion' differs from '$version'"
}
if (-not $buildScript.Contains(('Alekrythae-Core-v' + $version + '-Windows-x64'))) {
    throw 'Release directory naming does not match current VERSION metadata.'
}
Write-Host "Version identity consistent: VERSION=$version, binary label=$infoVersion."
