# Ałek’ryŧhæ Core: official Microsoft .NET 10 SDK bootstrap for Windows x64.
# Uses Microsoft's release metadata, a SHA-512 checked SDK ZIP, and a per-user installation.
# Never executes a downloaded script or needs administrator privileges.
$ErrorActionPreference = 'Stop'

function Test-Sdk10([string] $DotnetExe) {
    if (-not $DotnetExe -or -not (Test-Path -LiteralPath $DotnetExe -PathType Leaf)) { return $false }
    try {
        $versions = @(& $DotnetExe --list-sdks 2>$null)
        if ($LASTEXITCODE -ne 0) { return $false }
        foreach ($entry in $versions) {
            # A stable .NET 10 SDK, not just the runtime or a prerelease SDK.
            if ([string]$entry -match '^10\.\d+\.\d+\s+\[') { return $true }
        }
    } catch { return $false }
    return $false
}

function Find-ExistingSdk10 {
    $paths = New-Object System.Collections.Generic.List[string]
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command -and $command.Source) { $paths.Add($command.Source) }
    if ($env:ProgramFiles) { $paths.Add((Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')) }
    if ($env:LOCALAPPDATA) { $paths.Add((Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')) }
    if ($env:USERPROFILE) { $paths.Add((Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')) }

    # Find a previous installation managed by this builder (without network access).
    if ($env:LOCALAPPDATA) {
        $base = Join-Path $env:LOCALAPPDATA 'Alekrythae\DotNetSdk'
        if (Test-Path -LiteralPath $base -PathType Container) {
            Get-ChildItem -LiteralPath $base -Directory -Filter 'sdk-10.*' -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending | ForEach-Object {
                    $paths.Add((Join-Path $_.FullName 'dotnet.exe'))
                }
        }
    }

    foreach ($candidate in ($paths | Select-Object -Unique)) {
        if (Test-Sdk10 $candidate) { return $candidate }
    }
    return $null
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'Alekrythae Core requires 64-bit Windows (x64).'
}

$available = Find-ExistingSdk10
if ($available) {
    Write-Host '[OK] .NET 10 SDK already installed; skipping download.'
    Write-Output $available
    return
}

if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA is unavailable: cannot install the SDK for this user.' }
Write-Host '[INFO] .NET 10 SDK not found. Installing the official Microsoft SDK for this Windows user.'
Write-Host '[INFO] This is needed only for compiling the source, not for running the published Core.'

# Windows PowerShell 5.1 may not negotiate TLS 1.2 unless explicitly enabled.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$metadataUri = 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
$metadata = Invoke-RestMethod -Uri $metadataUri -UseBasicParsing -TimeoutSec 90
if ($metadata.'channel-version' -ne '10.0') { throw 'Unexpected Microsoft SDK release channel metadata.' }
$sdkVersion = [string]$metadata.'latest-sdk'
if ($sdkVersion -notmatch '^10\.\d+\.\d+$') { throw 'Invalid SDK version in official release metadata.' }

# The latest SDK is recorded under the matching release's sdk object.
$sdk = $null
foreach ($release in $metadata.releases) {
    if ($release.sdk -and [string]$release.sdk.version -eq $sdkVersion) {
        $sdk = $release.sdk
        break
    }
}
if (-not $sdk) { throw "Microsoft release metadata does not contain SDK $sdkVersion." }
$package = @($sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -eq 'dotnet-sdk-win-x64.zip' }) | Select-Object -First 1
if (-not $package) { throw 'Official Windows x64 SDK ZIP was not found.' }
$expectedUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"
if ([string]$package.url -cne $expectedUrl) { throw 'SDK package URL does not match the expected Microsoft origin.' }
if ([string]$package.hash -notmatch '^[0-9a-fA-F]{128}$') { throw 'SDK package does not have a valid SHA-512 digest.' }

$installBase = Join-Path $env:LOCALAPPDATA 'Alekrythae\DotNetSdk'
$installDir = Join-Path $installBase ('sdk-' + $sdkVersion)
$targetExe = Join-Path $installDir 'dotnet.exe'
if (Test-Sdk10 $targetExe) {
    Write-Host "[OK] Using previously installed .NET SDK $sdkVersion."
    Write-Output $targetExe
    return
}
if (Test-Path -LiteralPath $installDir) {
    throw "An incomplete SDK directory already exists: $installDir. Remove that directory after checking its contents, then retry."
}

New-Item -ItemType Directory -Path $installBase -Force | Out-Null
$tempDir = Join-Path $installBase ('install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir | Out-Null
$zipPath = Join-Path $tempDir 'sdk.zip'
$unpackDir = Join-Path $tempDir 'sdk'
try {
    Write-Host "[INFO] Downloading official .NET SDK $sdkVersion (once; this can take a few minutes)..."
    Invoke-WebRequest -Uri $expectedUrl -OutFile $zipPath -UseBasicParsing -TimeoutSec 1800
    $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA512).Hash
    if (-not [string]::Equals($actualHash, [string]$package.hash, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SDK download SHA-512 verification FAILED. The archive will not be extracted.'
    }
    Write-Host '[OK] Download verified against Microsoft SHA-512 release metadata.'
    Expand-Archive -LiteralPath $zipPath -DestinationPath $unpackDir
    if (-not (Test-Sdk10 (Join-Path $unpackDir 'dotnet.exe'))) {
        throw 'Extracted SDK did not report a stable .NET 10 SDK.'
    }
    Move-Item -LiteralPath $unpackDir -Destination $installDir
    if (-not (Test-Sdk10 $targetExe)) { throw 'The .NET SDK failed validation after installation.' }
    Write-Host "[OK] .NET SDK $sdkVersion ready: $installDir"
    Write-Output $targetExe
} finally {
    # Only delete the private temporary directory created by this invocation.
    if (Test-Path -LiteralPath $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
