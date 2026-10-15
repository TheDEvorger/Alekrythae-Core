param(
    [string]$TestHostDirectory, [string]$AppDirectory,
    [switch]$Joa, [switch]$Palette, [switch]$Loading, [switch]$Scenes,
    [switch]$F1, [switch]$Previews, [switch]$Soak, [switch]$Performance,
    [switch]$Opening, [ValidateRange(30,900)][int]$TimeoutSeconds=75
)
$ErrorActionPreference = 'Stop'
# The production startup calls Show/Activate/Focus/Topmost and shares a mutex/pipe.
# Only the separate test executable is allowed; there is no visible-host fallback.
if (-not $TestHostDirectory) { throw 'Desktop tests blocked: supply TestHostDirectory for the private desktop test host.' }
$coreRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = [IO.Path]::GetFullPath($TestHostDirectory)
$exe = Join-Path $release 'Core.DesktopTests.exe'
$policy = Get-Content -Raw -LiteralPath (Join-Path $release 'host-policy.json') | ConvertFrom-Json
if ($policy.protocol -ne 'joa-private-desktop-v1' -or $policy.productionBootstrap -ne $false -or $policy.desktopSwitch -ne $false) {
    throw 'Unsupported test host isolation policy.'
}
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Test executable missing: $exe" }
$fixture = Join-Path $coreRoot ('test-output/native-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $fixture | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'test-host-options.json'), ('{"timeoutSeconds":' + $TimeoutSeconds + '}'))
$appFile = Join-Path $fixture 'native-smoke.alek'
Copy-Item -LiteralPath (Join-Path $coreRoot 'tests/native-smoke.alek') -Destination $appFile
if ($Opening -and ($Palette -or $Loading -or $Scenes -or $F1 -or $Previews -or $Soak -or $Performance)) {
    throw 'Opening must run separately from fixtures that bypass or replace the opening.'
}
if ($Joa -or $Palette -or $Loading -or $Scenes -or $F1 -or $Previews -or $Soak -or $Performance -or $Opening) {
    $repository = Split-Path $coreRoot -Parent
    if ($AppDirectory) {
        $appRoot = [IO.Path]::GetFullPath($AppDirectory)
        $releaseRoot = Join-Path $repository 'release'
        if (-not $appRoot.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Packaged app test root must be under this repository release folder.'
        }
        $repository = $appRoot
    }
    Copy-Item -LiteralPath (Join-Path $repository 'Alekrythae.App') -Destination (Join-Path $fixture 'Alekrythae.App') -Recurse
    Copy-Item -LiteralPath (Join-Path $repository 'Assets') -Destination (Join-Path $fixture 'Assets') -Recurse
    $entry = [IO.File]::ReadAllText((Join-Path $repository 'Alekrythae.alek'))
    $probeFile = if ($Opening) { 'tests/scene-opening-probe.js' }
        elseif ($Performance) { 'tests/scene-performance-probe.js' }
        elseif ($Soak) { 'tests/scene-soak-probe.js' }
        elseif ($Loading) { 'tests/adventure-load-probe.js' }
        elseif ($Previews) { 'tests/scene-preview-probe.js' }
        elseif ($F1) { 'tests/scene-f1-probe.js' }
        elseif ($Scenes) { 'tests/scene-ui-probe.js' }
        elseif ($Palette) { 'tests/palette-layout-probe.js' }
        else { 'tests/joa-boot-probe.js' }
    if ($Loading -or $Scenes -or $F1 -or $Previews -or $Soak -or $Performance -or $Opening) {
        $legacyPath = Join-Path $fixture 'Alekrythae.App/legacy/legacy-app.js'
        $legacy = [IO.File]::ReadAllText($legacyPath)
        $marker = 'const createAdventureRecord=()=>{'
        if (-not $legacy.Contains($marker)) { throw 'Native load fixture injection point missing.' }
        $accessFile = if ($Opening) { 'tests/scene-opening-access.js' }
            elseif ($Scenes -or $F1 -or $Previews -or $Soak -or $Performance) { 'tests/scene-ui-access.js' }
            else { 'tests/adventure-load-access.js' }
        $access = [IO.File]::ReadAllText((Join-Path $coreRoot $accessFile))
        [IO.File]::WriteAllText($legacyPath, $legacy.Replace($marker, $access + [Environment]::NewLine + $marker), (New-Object Text.UTF8Encoding($false)))
    }
    $probe = [IO.File]::ReadAllText((Join-Path $coreRoot $probeFile))
    if ($Performance) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/scene-static-baseline.js')) + [Environment]::NewLine + $probe }
    if ($Soak) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/scene-soak-seed.js')) + [Environment]::NewLine + $probe }
    if ($Scenes) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/scene-render-bench.js')) + [Environment]::NewLine + $probe }
    if ($Scenes) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/scene-session-probe.js')) + [Environment]::NewLine + $probe }
    if ($Scenes) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/scene-edit-transition-probe.js')) + [Environment]::NewLine + $probe }
    if ($Scenes) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/scene-create-probe.js')) + [Environment]::NewLine + $probe }
    if ($Scenes) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/scene-copy-probe.js')) + [Environment]::NewLine + $probe }
    if ($Loading) { $probe = [IO.File]::ReadAllText((Join-Path $coreRoot 'tests/adventure-record-probe.js')) + [Environment]::NewLine + $probe }
    [IO.File]::WriteAllText($appFile, $entry + [Environment]::NewLine + $probe, (New-Object Text.UTF8Encoding($false)))
}
# Each fixture gets its own browser profile/logs and test-only executable copy.
$fixtureHost = Join-Path $fixture 'Host'
New-Item -ItemType Directory -Path $fixtureHost | Out-Null
Get-ChildItem -LiteralPath $release | Where-Object { $_.Name -ne 'KozmikData' -and $_.Extension -ne '.log' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $fixtureHost -Recurse }
$exe = Join-Path $fixtureHost 'Core.DesktopTests.exe'
$release = $fixtureHost
$arguments = '--run "' + $appFile + '"'
$phaseCount = if ($Soak) { 4 } else { 1 }
for ($phase = 0; $phase -lt $phaseCount; $phase++) {
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $release -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(($TimeoutSeconds+25)*1000)) {
    Stop-Process -Id $process.Id -Force
    throw "Native smoke timed out. Inspect logs in $release; fixture=$fixture"
}
if ($process.ExitCode -ne 0) {
    $hostError = Join-Path $fixture 'host-error.json'
    if (Test-Path -LiteralPath $hostError) { throw ([IO.File]::ReadAllText($hostError)) }
    throw "Test host exited with code $($process.ExitCode); fixture=$fixture"
}
& {
    $isolation = Get-Content -Raw -LiteralPath (Join-Path $fixture 'host-isolation.json') | ConvertFrom-Json
    $window = Get-Content -Raw -LiteralPath (Join-Path $fixture 'host-window.json') | ConvertFrom-Json
    if ($isolation.protocol -ne 'joa-private-desktop-v1' -or -not $isolation.inputUnchanged -or
        $isolation.productionBootstrap -ne $false -or $isolation.desktopSwitch -ne $false -or
        $isolation.testDesktop -notlike 'JOA-Test-*' -or $isolation.testDesktop -eq $isolation.inputBefore -or
        $window.desktop -ne $isolation.testDesktop) { throw 'Private desktop isolation evidence failed.' }
}
$result = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'native-result.json') | ConvertFrom-Json
if (-not $result.ok) { throw $result.error }
if ($Opening -and $result.kind -ne 'scene-opening') { throw 'Wrong opening probe ran.' }
if ($Performance -and $result.kind -ne 'scene-performance') { throw 'Wrong performance probe ran.' }
if ($Soak -and ($result.kind -ne 'scene-populated-soak' -or $result.phase -ne $phase)) { throw 'Wrong populated session phase.' }
if ($Previews -and $result.kind -ne 'scene-ui-previews') { throw 'Wrong preview probe ran.' }
if ($F1 -and $result.kind -ne 'scene-f1-geometry') { throw 'Wrong F1 acceptance probe ran.' }
if ($Scenes -and $result.kind -ne 'scene-ui-geometry') { throw 'Wrong scene acceptance probe ran.' }
if ((Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'tail.txt')) -ne 'drained before exit') { throw 'Pending write lost at shutdown.' }
if ($Soak) {
    foreach ($name in @('native-result.json','host-isolation.json','host-window.json','host-metrics.json')) {
        Copy-Item -LiteralPath (Join-Path $fixture $name) -Destination (Join-Path $fixture ("phase-$phase-$name"))
    }
    Write-Output "PASS populated phase=$phase totalTrips=$($result.totalTrips) priorProcessRestored=$($result.restoredPriorProcess). Fixture=$fixture"
}
elseif ($Opening) { Write-Output "PASS embedded opening/portal: previews=$(@($result.previews).Count), trusted foreground input not performed. Fixture=$fixture" }
elseif ($Performance) { Write-Output "PASS paired Canvas2D performance/pixels: rounds=$(@($result.rows).Count). Fixture=$fixture" }
elseif ($Previews) { Write-Output "PASS embedded WebView2 previews: count=$(@($result.previews).Count). Fixture=$fixture" }
elseif ($F1) { Write-Output "PASS real Core/WebView2 F1 geometry: cases=$($result.cases), screenshot acceptance not performed. Fixture=$fixture" }
elseif ($Scenes) { Write-Output "PASS real Core/WebView2 scene UI geometry: cases=$($result.cases), screenshot acceptance not performed. Fixture=$fixture" }
elseif ($Loading) { Write-Output "PASS adventure loading in real Core/WebView2: cases=$($result.cases), recordChecks=$($result.recordChecks), staged rollback, retry, return, shutdown drain. Fixture=$fixture" }
elseif ($Palette) { Write-Output "PASS palette in real Core/WebView2: layout cases=$($result.cases), live slider/focus, shutdown drain. Fixture=$fixture" }
elseif ($Joa) { Write-Output "PASS JOA in real Core/WebView2: phase=$($result.phase), modules=$(@($result.modules).Count), shutdown drain. Fixture=$fixture" }
else { Write-Output "PASS real Core/WebView2: legacy, Unicode, SQLite, sandbox, burst, shutdown drain. Fixture=$fixture" }

}
