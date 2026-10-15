# Ałek’ryŧhæ Core v2.1.0 | version-aligned source

## v2.1.0 automatic WebView2 prerequisite handling (source update, 2026-10-14)

- `BUILD_CORE.cmd` now detects the WebView2 Evergreen Runtime in addition to the .NET 10 SDK. An existing valid WebView2 Runtime installation is left unchanged.
- When absent, `tools/Ensure-WebView2.ps1` fetches the official Microsoft Evergreen Bootstrapper and validates the Microsoft Authenticode signature before silent installation and registry recheck.
- Published Core carries `Ensure-WebView2.ps1` and checks the runtime when opening a new `.alek` window, not when registering the `.alek` association.
- Download, signature, installation and post-install verification failures are surfaced instead of silently continuing.
- Source changes are prepared, but first-time installation and published WPF startup still require real clean-machine Windows verification. No version change beyond **v2.1.0**.

## v2.1.0 targeted security hardening (source candidate, 2026-10-10)

- On `dev.workspace.release`, revoke and dispose associated ConPTY terminal sessions; warn that native terminals are not sandboxed and require a separate prompt for administrator elevation.
- Deny unreadable workspace path attributes and revalidate the shell starting directory; preserve existing `dev.*` operation names.
- Exclude junction/symlink entries from `fs.list`; revalidate root-scoped file destinations immediately before supported atomic writes.
- Apply SQLite `trusted_schema = OFF` to ordinary DB connections and both `trusted_schema = OFF` and `query_only = ON` to transfer read-only connections.
- Harden `.alekdata` ZIP extraction against repeated/case-alias paths, Windows device names, dot/space suffixes and ZIP symlink entries; validate the actual streamed uncompressed bytes against declared limits.
- Reject undeclared content in version 2 manifests; reject oversized portable JSON/manifest files; exclude reparse points during export.
- Add adversarial archive and junction-listing regression fixtures. **These tests have been written but not executed successfully here because the Windows .NET 10 toolchain is unavailable.**
- This does **not** fix or verify the earlier Windows startup failure. See `docs/SECURITY_HARDENING_v2.1.0_TR.md`.

## v2.1.0 source metadata alignment — 2026-10-10

- Align `VERSION`, `.csproj` `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`, and Windows `Program.DisplayName`/`DisplayVersion`.
- Align the release packaging filename and metadata verification gate with v2.1.0.
- Refresh the README showroom, security policy and source/release hygiene documentation.
- The original version-alignment work preserved implementation behavior. A subsequent security-hardening pass introduced the targeted boundary changes listed above while retaining public bridge operation names.
- **Release status:** source archive only. No new Windows x64 binary build, integration pass, code-signature or clean-machine launch has been verified as part of this version-alignment work.

## Engineering baseline — 2026-09-25

Storage work leaves the UI thread and drains before shutdown. File/database paths share a sandbox policy; long Unicode Windows paths are supported. Registry, documents, folders, SQLite, bridge parsing/client, logging and file selection have separate responsibilities. Existing APIs, SQLite schemas and R9 data remain compatible in the regression suite. Builds produce a new release folder instead of deleting installed profiles. See [engineering details and limits](ENGINEERING_NOTES_TR.md).

Earlier hotfix work was part of the v2.0.0 lineage. The present source includes subsequent storage and lifecycle improvements; no fresh Windows release test is implied by these notes.

## Highlights

- Native Windows host for `.alek` applications
- WebView2 application runtime
- Portable SQLite-backed application and Adventure data
- Root-scoped filesystem bridge with traversal/reparse-point checks
- Atomic write protection on supported write paths
- `.alekdata` export/import safeguards with snapshot, verification and rollback handling
- GPU adapter discovery and Windows graphics preference integration
- Controlled external media selection, approval and probing
- Developer workspace APIs and Windows ConPTY terminal support
- External AI workspace window coordination
- ViodCera native bridge for resident hotkeys, OCR selection and popup lifecycle
- Explicit host lifecycle support through `app.exit`
- R7 recovery for abandoned single-instance mutexes after hard Core termination
- R7 single-instance identity separated from R6 so a stale R6 process cannot block the repaired runtime
- `.alek` association registration only when Core is launched directly, preventing temporary/debug copies from silently hijacking the file association
- WebView2 startup/close race guards to prevent disposed-object callbacks during shutdown
- `OperationCanceled` navigation noise filtered when WebView2 replaces its initial blank navigation

## Build

Extract the archive, then run:

```bat
BUILD_CORE.cmd
```

The build produces:

```text
release/Alekrythae-Core-v2.1.0-Windows-x64/
```

## Release identity

- Project/source version: `v2.1.0`
- Binary label upon a successful Windows build: `2.1.0` (unverified in this source-only package)
- Platform: Windows x64

See `README.md` for full architecture documentation and `SECURITY.md` for private vulnerability reporting.

## Maintainer validation notice (2026-10-10)

The version alignment alone did not alter runtime algorithms; the additional security-hardening pass **does** change selected runtime boundaries and must pass Windows compatibility and launch testing. Build/test evidence and a verified user-launch scenario are still required before a new binary is announced. See [Release Checklist](docs/SECURITY_CHECKLIST.md).
