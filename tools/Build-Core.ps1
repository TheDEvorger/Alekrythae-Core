param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$coreRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $coreRoot 'src/Alekrythae.Core/Alekrythae.Core.csproj'
$tests = Join-Path $coreRoot 'tests/Core.Compatibility/Core.Compatibility.csproj'
$releaseRoot = Join-Path $coreRoot 'release'
$releaseName = 'Alekrythae-Core-v2.1.0-Windows-x64'
$outputDirectory = Join-Path $releaseRoot $releaseName
$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ('AlekCorePublish-' + [Guid]::NewGuid().ToString('N'))
$stagingPackage = Join-Path $stagingRoot 'package'
if (Test-Path -LiteralPath $outputDirectory) {
    $releaseName += '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 4)
    $outputDirectory = Join-Path $releaseRoot $releaseName
}
# An installed release may contain real browser profiles. Always publish into a new directory.
if (-not ([IO.Path]::GetFullPath($outputDirectory)).StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output escaped the Core release folder.'
}
# Validate prerequisite scripts with the native Windows PowerShell parser before executing them.
# This catches quote/encoding regressions before any download or install attempt.
foreach ($scriptName in @('Ensure-DotNet10.ps1', 'Ensure-WebView2.ps1')) {
    $prerequisiteScript = Join-Path $PSScriptRoot $scriptName
    $parseTokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $prerequisiteScript, [ref]$parseTokens, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        $problem = $parseErrors[0]
        throw ("Invalid PowerShell syntax in {0} at line {1}: {2}" -f             $scriptName, $problem.Extent.StartLineNumber, $problem.Message)
    }
}
Write-Host '[OK] PowerShell prerequisite syntax verified.'
# Skip SDK download when .NET 10 is already installed; otherwise install to user-local storage.
$dotnet = & (Join-Path $PSScriptRoot 'Ensure-DotNet10.ps1')
if (-not $dotnet -or -not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw 'The .NET 10 SDK bootstrap did not return a usable dotnet executable.'
}
# Ensure the embedded-browser prerequisite without reinstalling it on machines that have it.
& (Join-Path $PSScriptRoot 'Ensure-WebView2.ps1')
if (-not $?) { throw 'WebView2 Runtime prerequisite check failed.' }
$env:DOTNET_ROOT = Split-Path -Path $dotnet -Parent
$env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $env:PATH
Push-Location -LiteralPath $coreRoot
try {
    & $dotnet restore $project --ignore-failed-sources --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Core restore failed.' }
    if (-not $SkipTests) {
        & $dotnet run --project $tests -c Release --no-launch-profile
        if ($LASTEXITCODE -ne 0) { throw 'Compatibility tests failed.' }
        $joaModule = Join-Path (Split-Path $coreRoot -Parent) 'Alekrythae.App/modules/adventures/persistence/atomic-documents.js'
        if (Test-Path -LiteralPath $joaModule) {
            & node --test (Join-Path $coreRoot 'tests/client-contract.test.cjs')
            if ($LASTEXITCODE -ne 0) { throw 'JOA/Core bridge test failed.' }
        }
    }
    # MSBuild item transforms cannot safely expand an apostrophe in an absolute PublishDir.
    # Build in a unique short path, then copy the completed package to the requested workspace.
    & $dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore --nologo -o $stagingPackage
    if ($LASTEXITCODE -ne 0) { throw 'Core publish failed.' }
    Copy-Item -LiteralPath $stagingPackage -Destination $outputDirectory -Recurse
    foreach ($required in @('Alekrythae Core.exe', 'Alekrythae Core.runtimeconfig.json', 'Alekrythae Core.deps.json', 'Ensure-WebView2.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputDirectory $required))) { throw "Missing runtime file: $required" }
    }
    $archive = Join-Path $releaseRoot ($releaseName + '.zip')
    $stagingArchive = Join-Path $stagingRoot 'Core-Release.zip'
    & tar.exe -a -cf $stagingArchive -C $stagingPackage .
    if ($LASTEXITCODE -ne 0) { throw 'Release ZIP failed.' }
    Copy-Item -LiteralPath $stagingArchive -Destination $archive
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($archive + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine)
    Write-Output "Release: $outputDirectory"
    Write-Output "Archive: $archive"
} finally {
    Pop-Location
    $resolvedStage = [IO.Path]::GetFullPath($stagingRoot)
    $expectedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\AlekCorePublish-'
    if ($resolvedStage.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
