param(
    [string]$InstallRoot = "",
    [string]$BundleRoot = "",
    [int]$ProcessId = 0,
    [ValidateSet("Ask", "CoreOnly", "Everything")]
    [string]$Mode = "Ask",
    [switch]$Quiet
)

$ErrorActionPreference = "SilentlyContinue"
$ProgressPreference = "SilentlyContinue"
$ProgId = "Alekrythae.Nexus.v0"
$CoreMarker = ".alekrythae-core-root"
$BundleMarker = ".alekrythae-bundle-root"
$LogPath = Join-Path $env:TEMP ("Alekrythae_Uninstall_{0}.log" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
$Failures = New-Object System.Collections.Generic.List[string]

function Write-Log([string]$Text) {
    try { Add-Content -LiteralPath $LogPath -Value ("[{0}] {1}" -f (Get-Date), $Text) -Encoding UTF8 } catch {}
}

function Show-Message([string]$Text, [string]$Title, [string]$Buttons = "OK", [string]$Icon = "Information") {
    if ($Quiet) { return $null }
    try {
        Add-Type -AssemblyName PresentationFramework | Out-Null
        $buttonValue = [System.Windows.MessageBoxButton]::$Buttons
        $iconValue = [System.Windows.MessageBoxImage]::$Icon
        return [System.Windows.MessageBox]::Show($Text, $Title, $buttonValue, $iconValue)
    } catch {
        return $null
    }
}

function Normalize-Path([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return "" }
    try { return [System.IO.Path]::GetFullPath($PathValue).TrimEnd('\', '/') } catch { return "" }
}

function Test-DangerousRoot([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return $true }
    $full = Normalize-Path $PathValue
    if ([string]::IsNullOrWhiteSpace($full)) { return $true }
    $driveRoot = [System.IO.Path]::GetPathRoot($full).TrimEnd('\', '/')
    if ($full.Equals($driveRoot, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }

    $dangerous = @(
        $env:WINDIR,
        $env:SystemRoot,
        $env:USERPROFILE,
        $env:LOCALAPPDATA,
        $env:APPDATA,
        $env:ProgramData,
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($item in $dangerous) {
        $normalized = Normalize-Path $item
        if ($full.Equals($normalized, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-CoreRoot([string]$PathValue) {
    $full = Normalize-Path $PathValue
    if (Test-DangerousRoot $full) { return $false }
    return (
        (Test-Path -LiteralPath (Join-Path $full $CoreMarker)) -or
        (Test-Path -LiteralPath (Join-Path $full "Alekrythae Core.exe")) -or
        (Test-Path -LiteralPath (Join-Path $full "Alekrythae Core.dll")) -or
        (Test-Path -LiteralPath (Join-Path $full "Alekrythae Core.csproj"))
    )
}

function Test-BundleRoot([string]$PathValue) {
    $full = Normalize-Path $PathValue
    if (Test-DangerousRoot $full) { return $false }
    return Test-Path -LiteralPath (Join-Path $full $BundleMarker)
}

function Remove-PathSafely([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue) -or -not (Test-Path -LiteralPath $PathValue)) { return }
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $PathValue -Recurse -Force -ErrorAction Stop
            Write-Log "Removed: $PathValue"
            return
        } catch {
            if ($attempt -eq 5) {
                $Failures.Add("Silinemedi: $PathValue - $($_.Exception.Message)")
                Write-Log "FAILED: $PathValue - $($_.Exception.Message)"
            } else {
                Start-Sleep -Milliseconds (400 * $attempt)
            }
        }
    }
}

function Remove-RegistryData {
    Write-Log "Registry cleanup started."
    try {
        $extKeyPath = "HKCU:\Software\Classes\.alek"
        if (Test-Path $extKeyPath) {
            $extKey = Get-Item $extKeyPath
            $defaultValue = [string]$extKey.GetValue("")
            if ($defaultValue -eq $ProgId) {
                Remove-Item $extKeyPath -Recurse -Force
            } else {
                Remove-ItemProperty -Path (Join-Path $extKeyPath "OpenWithProgids") -Name $ProgId -Force
            }
        }
        Remove-Item "HKCU:\Software\Classes\$ProgId" -Recurse -Force
        Remove-Item "HKCU:\Software\Classes\Applications\Alekrythae Core.exe" -Recurse -Force
        Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AlekrythaeCore" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AlekrythaeCoreV000" -Recurse -Force -ErrorAction SilentlyContinue
    } catch {
        $Failures.Add("Dosya ilişkilendirmesi tamamen temizlenemedi: $($_.Exception.Message)")
    }

    try {
        $gpuKeyPath = "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences"
        if (Test-Path $gpuKeyPath) {
            $gpuKey = Get-Item $gpuKeyPath
            foreach ($valueName in $gpuKey.GetValueNames()) {
                if ($valueName -match "(?i)Alekrythae Core\.exe$" -or
                    (-not [string]::IsNullOrWhiteSpace($InstallRoot) -and $valueName.StartsWith($InstallRoot, [System.StringComparison]::OrdinalIgnoreCase))) {
                    Remove-ItemProperty -Path $gpuKeyPath -Name $valueName -Force
                }
            }
        }
    } catch {
        $Failures.Add("GPU tercihi tamamen temizlenemedi: $($_.Exception.Message)")
    }
}

function Remove-Shortcuts {
    $shortcutRoots = @(
        [Environment]::GetFolderPath("Desktop"),
        [Environment]::GetFolderPath("CommonDesktopDirectory"),
        [Environment]::GetFolderPath("StartMenu"),
        [Environment]::GetFolderPath("CommonStartMenu")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

    foreach ($root in $shortcutRoots) {
        try {
            Get-ChildItem -LiteralPath $root -Filter "*.lnk" -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match "(?i)Alekrythae|Ałek" } |
                ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
        } catch {
            $Failures.Add("Kısayollar tamamen temizlenemedi: $root")
        }
    }
}

function Remove-Caches {
    $cachePaths = @(
        (Join-Path $env:LOCALAPPDATA "AlekrythaeCore"),
        (Join-Path $InstallRoot "KozmikData"),
        (Join-Path $InstallRoot "Alekrythae Core.exe.WebView2"),
        (Join-Path $InstallRoot "AlekrythaeCore.exe.WebView2"),
        (Join-Path $InstallRoot "EBWebView")
    )
    foreach ($path in $cachePaths) { Remove-PathSafely $path }

    foreach ($logFile in @(
        (Join-Path $InstallRoot "alekrythae_crash.log"),
        (Join-Path $InstallRoot "alekrythae_webview.log")
    )) {
        if (Test-Path -LiteralPath $logFile) {
            try { Remove-Item -LiteralPath $logFile -Force } catch {}
        }
    }
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$InstallRoot = Normalize-Path $InstallRoot
$BundleRoot = Normalize-Path $BundleRoot
try { Set-Location -LiteralPath $env:TEMP } catch {}

if (-not (Test-CoreRoot $InstallRoot)) {
    Show-Message "Güvenlik nedeniyle kaldırma durduruldu. Geçerli Ałek’ryŧhæ Core klasörü doğrulanamadı.`n`n$InstallRoot" "Ałek’ryŧhæ Kaldırıcı" "OK" "Error" | Out-Null
    Write-Log "Invalid core root: $InstallRoot"
    exit 2
}

if ([string]::IsNullOrWhiteSpace($BundleRoot) -or -not (Test-BundleRoot $BundleRoot)) {
    $cursor = Get-Item -LiteralPath $InstallRoot
    for ($depth = 0; $cursor -ne $null -and $depth -lt 10; $depth++) {
        if (Test-BundleRoot $cursor.FullName) {
            $BundleRoot = $cursor.FullName
            break
        }
        $cursor = $cursor.Parent
    }
}
if ([string]::IsNullOrWhiteSpace($BundleRoot) -or -not (Test-BundleRoot $BundleRoot)) {
    $BundleRoot = $InstallRoot
}

if ($Mode -eq "Ask") {
    $first = Show-Message "Ałek’ryŧhæ Core bilgisayardan kaldırılacak. Devam edilsin mi?" "Ałek’ryŧhæ Core'u Kaldır" "YesNo" "Warning"
    if ($first -ne [System.Windows.MessageBoxResult]::Yes) { exit 0 }

    $choice = Show-Message "Paket ve kullanıcı verileri de silinsin mi?`n`nEVET: Core + paket + bütün Data/Media/Journey/.alek dosyaları.`nHAYIR: Yalnız Core, önbellek, kayıtlar ve kısayollar.`nİPTAL: Vazgeç." "Kaldırma Türü" "YesNoCancel" "Warning"
    if ($choice -eq [System.Windows.MessageBoxResult]::Cancel) { exit 0 }
    $Mode = if ($choice -eq [System.Windows.MessageBoxResult]::Yes) { "Everything" } else { "CoreOnly" }
}

Write-Log "Mode=$Mode InstallRoot=$InstallRoot BundleRoot=$BundleRoot ProcessId=$ProcessId"

if ($ProcessId -gt 0) {
    try { Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction Stop } catch { Start-Sleep -Seconds 2 }
}

Remove-RegistryData
Remove-Shortcuts
Remove-Caches

$deleteTarget = if ($Mode -eq "Everything") { $BundleRoot } else { $InstallRoot }
if ($Mode -eq "Everything" -and -not (Test-BundleRoot $deleteTarget) -and -not (Test-CoreRoot $deleteTarget)) {
    $Failures.Add("Tam kaldırma hedefi güvenli biçimde doğrulanamadı: $deleteTarget")
} elseif ($Mode -eq "CoreOnly" -and -not (Test-CoreRoot $deleteTarget)) {
    $Failures.Add("Core kaldırma hedefi güvenli biçimde doğrulanamadı: $deleteTarget")
} else {
    Remove-PathSafely $deleteTarget
}

try {
    Start-Process -FilePath "$env:SystemRoot\System32\ie4uinit.exe" -ArgumentList "-ClearIconCache" -WindowStyle Hidden
} catch {}

if ($Failures.Count -eq 0) {
    $message = if ($Mode -eq "Everything") {
        "Ałek’ryŧhæ Core, paket, kullanıcı verileri, kayıtlar, önbellek ve kısayollar kaldırıldı."
    } else {
        "Ałek’ryŧhæ Core, kayıtlar, önbellek ve kısayollar kaldırıldı. Paket ve macera verileri korundu."
    }
    Show-Message $message "Kaldırma Tamamlandı" "OK" "Information" | Out-Null
    Write-Log "Uninstall completed successfully."
} else {
    $details = ($Failures -join "`n")
    Show-Message "Kaldırma büyük ölçüde tamamlandı; bazı parçalar temizlenemedi.`n`n$details`n`nGünlük: $LogPath" "Kaldırma Tamamlandı" "OK" "Warning" | Out-Null
}

try { Remove-Item -LiteralPath $PSCommandPath -Force } catch {}
exit 0
