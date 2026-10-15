# 🛡️ Maintainer security & release checklist

**Status:** Template. Every box below starts unchecked; inclusion of this file does **not** mean the control has passed.

## Source and dependency review

- [ ] `git status --short` contains no unexpected generated files or user data.
- [ ] `pwsh -File tools/Verify-SourceHygiene.ps1` succeeds on a real Git checkout.
- [ ] No credentials, keys, tokens, logs, crash dumps, WebView2 profiles or production DBs are in Git history.
- [ ] NuGet dependencies, licenses, updates and advisories are reviewed.
- [ ] GitHub secret scanning, branch protection, CodeQL and Dependabot are configured and checked.
- [ ] Changes to `src/Alekrythae.Core/Resources/**` retain all required canonical visual assets.

## Runtime security

- [ ] Trusted and deliberately malicious `.alek` fixtures are exercised on Windows x64.
- [ ] External top-level navigation and popup requests are blocked while legitimate generated startup navigation succeeds.
- [ ] Embedded bridge cannot be invoked from a foreign origin or stale document instance.
- [ ] Privileged `dev.*` operations and ConPTY shell access have explicit application-trust review.
- [ ] `dev.workspace.release` terminates its ConPTY shells; denied approvals start no shell; elevated requests require separate confirmation.
- [ ] ZIP import rejects duplicate/case-alias paths, links, device names, v2 undeclared files, oversized manifests; valid v2 exports still round-trip.
- [ ] Export skips reparse points; SQLite `PRAGMA trusted_schema` remains off and existing databases/schema checks still work.
- [ ] Sandbox traversal, `..`, UNC/device, ADS, junction, symlink, long paths and race windows are tested.
- [ ] SQLite sidecars and `.alekdata` imports cannot escape the root; ZIP-bombs/malformed manifests are handled.
- [ ] Atomic writes preserve the old file on expected failure; shutdown drains accepted writes.
- [ ] Logs/dumps do not expose sensitive media contents, user text, credentials or tokens.
- [ ] Registry/file association and uninstall cleanup preserve unrelated files and previous installations.

## Packaging and verification

- [ ] Version is consistent across `VERSION`, `.csproj`, scripts, README and release notes.
- [ ] Build is completed on a clean Windows x64 machine with the declared .NET SDK.
- [ ] Source builds in a Unicode/apostrophe-containing checkout path.
- [ ] Tests pass, including the actual `tests/Core.Compatibility` console runner.
- [ ] WebView2 runtime installation/absence and Windows 10/11 launch are tested.
- [ ] Fresh launch, double-click `.alek`, existing-data migration, exit, restart and uninstall have been tested.
- [ ] ZIP contains compiled executable **and** every required publish dependency.
- [ ] SHA-256 is generated from the *final* release ZIP; user verifies it from a trustworthy channel.
- [ ] Any Authenticode signature is validated; if not signed, release says so explicitly.
- [ ] Source ZIP and executable ZIP are labeled separately; source snapshot is never advertised as a runnable installer.
- [ ] Maintainer assigns support/release status and approves publication.

## Failure triage

If launch fails, retain *sanitized* startup logs, Windows version, WebView2 version, exact Core binary file version, `.alek` file structure and the command run. First determine whether failure is caused by absent WebView2, wrong `.alek` path, version drift, startup navigation, missing publish files, write permissions or a real exception. **Do not announce a fix without reproducing and validating it.**

## Microsoft WebView2 first-run prerequisite checks (2026-10-14)

- [ ] On Windows x64 with WebView2 installed, `BUILD_CORE.cmd` skips the installer without modifying the installation.
- [ ] On a disposable clean Windows x64 machine with WebView2 absent, the script downloads only Microsoft’s bootstrapper, checks Authenticode, installs and rechecks the registry.
- [ ] On the published release, the `Ensure-WebView2.ps1` companion exists alongside `Alekrythae Core.exe`.
- [ ] On first `.alek` launch without WebView2, the published host completes setup and opens its UI; with no internet or invalid signature it displays a clear error without launching the installer.
- [ ] Test no-admin operation and restrictive enterprise/antivirus policies; document blocked-install behavior.
- [ ] Confirm app association-only launch does not trigger WebView2 installation.
