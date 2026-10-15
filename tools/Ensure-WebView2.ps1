#Requires -Version 5.1
# Alekrythae Core: verifies the Microsoft Edge WebView2 Evergreen Runtime.
# When absent, downloads Microsoft's signed Evergreen Bootstrapper and runs it.
$ErrorActionPreference = 'Stop'

function Test-WebView2Runtime {
    $appId = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    $locations = @(
        "Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\EdgeUpdate\Clients\$appId",
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$appId",
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\$appId"
    )
    foreach ($location in $locations) {
        try {
            $property = Get-ItemProperty -LiteralPath $location -Name pv -ErrorAction Stop
            $version = [version]([string]$property.pv)
            if ($version -gt [version]'0.0.0.0') { return $true }
        } catch { } # Missing/malformed registry value is not a valid installation.
    }
    return $false
}

if (Test-WebView2Runtime) {
    Write-Host '[OK] Microsoft Edge WebView2 Runtime is already installed; skipping setup.'
    return
}
if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'Alekrythae Core requires a 64-bit Windows installation.'
}
Write-Host '[INFO] WebView2 Runtime is missing. Downloading the official Microsoft Evergreen Bootstrapper...'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# The Microsoft-published 'Get the Link' URL for the Evergreen Bootstrapper.
# Code signing is verified on the downloaded executable before it can be run.
$officialUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
$work = Join-Path ([IO.Path]::GetTempPath()) ('Alekrythae-WebView2-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$setup = Join-Path $work 'MicrosoftEdgeWebview2Setup.exe'
try {
    Invoke-WebRequest -Uri $officialUrl -OutFile $setup -UseBasicParsing -TimeoutSec 300
    $auth = Get-AuthenticodeSignature -LiteralPath $setup
    if ($auth.Status -ne 'Valid' -or -not $auth.SignerCertificate) {
        throw ('Downloaded WebView2 installer has no valid Authenticode signature: ' + $auth.Status)
    }
    $subject = [string]$auth.SignerCertificate.Subject
    if ($subject -notmatch '(?:^|,\s*)(?:CN|O)=Microsoft Corporation(?:,|$)') {
        throw ('WebView2 installer is not signed by Microsoft Corporation: ' + $subject)
    }
    Write-Host '[OK] Microsoft digital signature is valid.'
    Write-Host '[INFO] Installing WebView2 Runtime silently; this may take a few minutes...'
    $process = Start-Process -FilePath $setup -ArgumentList @('/silent','/install') -PassThru
    try {
        if (-not $process.WaitForExit(600000)) {
            try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
            throw 'Microsoft WebView2 setup did not complete within 10 minutes.'
        }
        $exitCode = $process.ExitCode
    } finally { $process.Dispose() }
    if ($exitCode -ne 0) {
        throw ('Microsoft WebView2 setup returned exit code ' + $exitCode + '.')
    }
    # The process exit code alone is insufficient; confirm the actual installation.
    $installed = $false
    for ($i = 0; $i -lt 15; $i++) {
        if (Test-WebView2Runtime) { $installed = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $installed) { throw 'WebView2 setup ended, but the Runtime registry version was not found.' }
    Write-Host '[OK] Microsoft Edge WebView2 Runtime installed and verified.'
} finally {
    if (Test-Path -LiteralPath $work -PathType Container) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
