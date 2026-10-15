#requires -Version 5.1
<#
    Fail-closed, local-first hygiene gate for tracked/candidate source content.
    A heuristic guard, not a substitute for GitHub secret scanning / dedicated tools.
    Run: pwsh -NoProfile -File tools/Verify-SourceHygiene.ps1
#>
[CmdletBinding()]
param([switch]$Strict)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$errors = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

$hasGit = (& git -C $root rev-parse --is-inside-work-tree 2>$null) -eq 'true'
if ($hasGit) {
    $paths = @(& git -C $root ls-files --cached --others --exclude-standard)
} else {
    $warnings.Add('No Git worktree detected: scanning visible source files; .gitignore exclusions cannot be fully validated.')
    $paths = @(Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) })
}
$paths = @($paths | Sort-Object -Unique)
$unsafePath = '(?i)(^|[/\\])(?:\.env(?:\..+)?|KozmikData|EBWebView|WebView2Data|WebView2Profile|CrashLogs|BrowserProfile|node_modules|obj|bin|TestResults|test-output|\.vs|\.idea|\.git)([/\\]|$)|(?i)\.(?:pfx|p12|pem|key|kdbx|dmp|mdmp|hdmp|sqlite|sqlite3|db-wal|db-shm|alekdata)$'
$patterns = @(
    @{ Name = 'PEM PRIVATE KEY'; Pattern = '-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----' },
    @{ Name = 'GitHub classic token'; Pattern = 'gh[pousr]_[A-Za-z0-9_]{30,}' },
    @{ Name = 'GitHub fine-grained token'; Pattern = 'github_pat_[A-Za-z0-9_]{30,}' },
    @{ Name = 'AWS access key ID'; Pattern = '(?<![A-Za-z0-9])(?:AKIA|ASIA)[A-Z0-9]{16}(?![A-Za-z0-9])' },
    @{ Name = 'Slack token'; Pattern = 'xox[baprs]-[0-9A-Za-z-]{20,}' }
)
$textExtensions = @('.cs','.js','.cjs','.mjs','.ts','.tsx','.json','.yaml','.yml','.xml','.config','.ps1','.cmd','.bat','.env','.properties','.ini','.toml','.txt','.md','.html','.css','.sh')
$scanned = 0
foreach ($p in $paths) {
    $relative = $p.Replace('\','/')
    if ($relative -match $unsafePath) { $errors.Add('Sensitive/generated path included: ' + $relative); continue }
    $absolute = Join-Path $root $p
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { continue }
    if ([IO.Path]::GetExtension($absolute) -notin $textExtensions) { continue }
    $item = Get-Item -LiteralPath $absolute
    if ($item.Length -gt 2MB) { $warnings.Add("Text file > 2 MiB skipped: $relative"); continue }
    $scanned++
    $content = [IO.File]::ReadAllText($absolute)
    foreach ($pattern in $patterns) {
        if ([regex]::IsMatch($content, $pattern.Pattern)) { $errors.Add("Possible $($pattern.Name) in $relative") }
    }
}
if ($hasGit) {
    $tracked = @(& git -C $root ls-files)
    foreach ($p in $tracked) {
        if ($p.Replace('\','/') -match $unsafePath) { $errors.Add("Already tracked sensitive path: $p") }
    }
}
foreach ($warning in $warnings) { Write-Warning $warning }
foreach ($error in $errors) { Write-Error $error -ErrorAction Continue }
Write-Host "Hygiene scan: $($paths.Count) candidate paths; $scanned text files; $($errors.Count) issues."
if ($errors.Count -gt 0) { exit 1 }
if ($Strict -and $warnings.Count -gt 0) { exit 2 }
exit 0
