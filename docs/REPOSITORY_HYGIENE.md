# 🧹 Repository Hygiene | The Source Is Not the Machine

Ałek’ryŧhæ Core must keep two independent worlds apart:

- **Reproducible product:** source, C# projects, scripts, test fixtures, canonical imagery, iconography, documentation and license.
- **Ephemeral machine state:** installed runtime caches, WebView2 profiles, saved documents, portable user databases, crash logs, private credentials, temporary release payloads and local builds.

## What the `.gitignore` means

The root [`.gitignore`](../.gitignore) uses 11 labeled sections and excludes developer-specific files, .NET outputs, diagnostic dumps, browser identity/cache, portable databases, `.alekdata` exports, generated archives, environment secrets, signing keys, dependency caches and OS litter.

Some **intentionally tracked** assets must remain in source control even if large: `Resources/Alekrythae.png`, `Bluemoon.png`, `Adventurer.png`, `*.ico`, source `.alek` fixtures, TypeScript/JavaScript bridge code, project files, script files and canonical template JSON. Treat those as production inputs rather than cleaning them out of Git.

### `.gitignore` is not a firewall

An ignore rule prevents an *untracked* file from appearing automatically in normal `git add` operations. It **does not** erase previously committed content or stop `git add -f`. It also does not stop a malicious file embedded inside an otherwise allowed archive or source file.

Check what Git will stage:

```powershell
git status --short
git add --dry-run .
git diff --cached --name-only
git check-ignore -v -- "release/MyPackage.zip"
```

To untrack a local file while keeping it on disk:

```powershell
git rm --cached -- "path/to/local-file"
```

If a secret was ever committed, **rotate it before any other cleanup**. History rewriting alone does not revoke a copied credential, and existing clones/forks may still retain it.

## Why cloning a repository may not reproduce someone's working installation

GitHub's source ZIP and `git clone` contain **committed tracked files**, not the originator's ignored files. A `.gitignore` generally does not prevent a clone from containing legitimate source assets, but a project that depends accidentally on ignored machine-specific files may fail on a clean computer.

For this Core the necessary `Resources` images/icons, `.csproj`, `.sln`, scripts and bridge code are intentionally tracked. A working installation additionally needs a successful .NET publish, WebView2 installed on Windows, a compatible `.alek` application and valid file permissions. Portable game/user data should be separately backed up, not moved into Git.

## Build output is not a source repository

The release builder generates a self-contained Windows host and ZIP in `release/`. The ZIP should contain **compiled runtime files**, not simply the GitHub source snapshot. Confirm at minimum:

- the actual Windows executable,
- `runtimeconfig.json` and `deps.json`,
- all necessary managed/native dependencies and runtime resources,
- the uninstall/metadata files required by the publish output,
- and an SHA-256 file derived from the final package.

A complete release still requires Windows smoke-testing, real app start/stop and basic install/update/uninstall checks.

## Auditing before publishing

```powershell
# Run from repository root with PowerShell 7 (Windows PowerShell 5.1 also supported by scripts)
pwsh -NoProfile -File .\tools\Verify-SourceHygiene.ps1
pwsh -NoProfile -File .\tools\Verify-ReleaseMetadata.ps1

git status --short
```

The hygiene scanner is a conservative pattern detector. A pass means no included candidate matched its limited list; it **cannot** prove that a repo contains no secrets. GitHub secret scanning, independent audit, manual review, and release-signing hygiene are separate layers.

## GitHub settings checklist

Enable repository security features as supported by the account: private vulnerability reporting, secret scanning/push protection, Dependabot alerts, dependency review, CodeQL, branch protection, and required status checks. Workflows in `.github/workflows` use read-only default content permissions; CodeQL requests `security-events: write` for findings. Critical third-party Actions should be periodically audited and ideally pinned to immutable reviewed SHAs.

Related documents: [Security policy](../SECURITY.md) · [Security sign-off checklist](SECURITY_CHECKLIST.md) · [Main showroom](../README.md).
