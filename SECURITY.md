# 🛡️ Ałek’ryŧhæ Core | Security Policy

> **One Core. Explicit boundaries. Responsible disclosure.**
>
> This document describes security responsibilities, supported release policy, coordinated vulnerability disclosure, and the controls used by the source repository. It is **not** a certification, security audit, or promise that a given build is vulnerability-free.

[Report privately](#reporting-a-vulnerability) · [Support](#supported-versions) · [Threat model](#threat-model) · [Implementation](#security-relevant-controls) · [Repository hygiene](#repository-and-build-supply-chain) · [Incident response](#coordinated-response)

---

## Supported versions

Security maintenance targets the **latest verified stable Windows x64 release** published by the maintainer. A source archive, tag, revision label, draft artifact, or README version is not proof of a tested production release.

| Release type | Security support |
|---|---|
| Latest maintainer-designated stable release | Eligible for assessment and fixes |
| Preview, development, or unverified ZIP/source package | No stable-release support guarantee |
| Superseded releases | Generally not maintained; may receive exceptional fixes at maintainer discretion |
| Modified, unofficial or third-party builds | Outside maintainer support |

**Packaging caveat (source received 2026-10-10):** the incoming source had mismatched version metadata. This updated **v2.1.0 source snapshot** aligns `VERSION`, C# assembly and display metadata, the release script and release notes. It remains **unverified as a runnable Windows release**. Confirm the built executable/file version and publisher provenance before distribution.

## Reporting a vulnerability

**Private security contact:** [thedevorger.alekrythae.dev@gmail.com](mailto:thedevorger.alekrythae.dev@gmail.com)

If GitHub's **Security → Advisories → Report a vulnerability** option is enabled, use that private workflow. Otherwise, email the address above with a neutral subject such as `Private security report | Alekrythae Core | <affected version>`.

**Do not use public Issues, Discussions, pull requests, or social media to disclose an unpatched exploit.** Never attach actual user documents, cookies, browser profiles, access tokens, private keys, personal databases, or confidential company data. Prefer synthetic data and a minimal reproduction.

A useful report contains:

1. Affected Core version, binary file version, Windows version, WebView2 runtime and (if relevant) `.alek` app version.
2. Impacted feature: `fs.*`, SQLite, `.alekdata`, developer shell, external media, bridge, hotkeys/OCR, AI docking, registration, or packaging.
3. Preconditions, entry point, required privileges, whether user interaction is needed, and precise steps to reproduce.
4. Expected versus observed behavior, sanitized traces, hashes of test artifacts and impact assessment.
5. A minimal **non-destructive** proof of concept using dummy accounts/files, including suggested mitigation if known.
6. A safe return contact and, optionally, preferred credit name and disclosure date.

Do not send exploit chains that target unrelated systems or run testing against devices you do not own or have explicit permission to assess. There is no published bug-bounty or compensation commitment.

## Coordinated response

The maintainer aims to:

1. Triage reports confidentially and confirm a reproducible security boundary violation.
2. Evaluate severity, affected versions and whether an upstream dependency is involved.
3. Prepare a tested fix or mitigating guidance without leaking technical exploit details.
4. Produce release notes and, where appropriate, a GitHub Security Advisory/CVE.
5. Publish coordinated details once a mitigation is available, or agree on a responsible timeline with the reporter.

**There is no contractual response-time, remediation-time, or availability SLA.** A reporting acknowledgment does not imply vulnerability confirmation. Do not publish private correspondence without consent.

---

## Threat model

Ałek’ryŧhæ Core hosts local `.alek` applications inside WebView2 and exposes explicitly implemented Windows capabilities. Security review must account for the difference between an application's JavaScript, trusted Core code, user-selected external paths, and native operating-system APIs.

| Asset / boundary | Principal risk | Intended mitigation or review requirement |
|---|---|---|
| `.alek` application content → native bridge | Untrusted script triggering privileged actions | Explicit operation routing, local document navigation constraints, no silent privilege promotion |
| App root → filesystem | Traversal, NTFS alternate streams, UNC/device paths, junction redirects | Root-scoped path validation, canonicalization, deny non-ordinary paths, adversarial path tests |
| App root → SQLite | Escaping the data root via DB paths or journal sidecars | Shared path policy and database sidecar checks |
| External media → portable world | Arbitrary disk access, untrusted formats, path persistence | User-driven file selection, controlled probing/import, copy into app-owned Media |
| Data transfer / `.alekdata` | ZIP traversal, corrupted packages, overwritten worlds, data loss | Validation, hashing, consistent SQLite snapshot, backup/rollback, conflict resolution |
| `dev.*` / ConPTY | Local command execution and workspace modification | Treat as **privileged development capability**, not as a harmless feature; test who can invoke it |
| WebView2 profile | Cookies, session state, credentials, caches | Separate profile, disabled browser autofill/password save in host, never commit runtime profiles |
| External AI browser | Account and session leakage | Window-level visual docking only; no intentional DOM, cookie, token or traffic extraction |
| Hotkeys / Windows OCR | Capturing sensitive screen content | User-triggered selection, minimize retention/logging, avoid background exfiltration |
| Update/release pipeline | Compromised binaries, malicious dependency/version drift | Source/test gates, dependency review, secret scanning, checksum publication, signed artifacts when available |

### What is and is not a security boundary

- The **filesystem sandbox** is an application path-control layer inside the Core process. It is **not** a Windows AppContainer, VM, or defense against a compromised native process with the same OS permissions.
- A `.alek` file is executable application content; **do not open an untrusted `.alek`** merely because the extension is familiar.
- `dev.shell.*` starts a native terminal session. That feature carries real operating-system authority and must never be described as safely executing arbitrary untrusted code.
- A SHA-256 checksum detects changed bytes **if obtained from a trusted channel**. It does not establish developer identity, authenticity, malware-freedom, or a signed release.
- GitHub Actions, a `.gitignore`, and private vulnerability reports improve hygiene but do not replace code review, Windows runtime testing, or penetration testing.

## Security-relevant controls in the submitted source

The following are **source-observed design/implementation details**, not a claim that the entire running system passed an independent audit:

**Embedded WebView2:** `CosmicGate.cs` configures a restricted application-oriented web surface (password autosave and autofill disabled, built-in context menu suppressed, production DevTools UI disabled). It blocks ordinary top-level navigation and new-window requests. Its R9 startup exception permits a short-lived generated `data:text/html` navigation; any change to this handshake requires regression tests because earlier restrictions reportedly caused black-screen startup failures.

**Path handling:** `Storage/SandboxPathPolicy.cs` rejects rooted paths, colons used by NTFS alternate data streams, traversal outside the configured root and detected reparse points. `DemandDatabase` also validates SQLite `-wal`, `-shm`, and `-journal` paths. Race conditions involving an external process modifying the directory tree remain a residual risk.

**Atomic writes:** supported file operations stage new content before replacement. This reduces partial-write risk; it is not guaranteed protection from sudden power loss, bad storage hardware, or all filesystem behaviors.

**Portable transfers:** `DataTransferService.cs` includes export/import validation, snapshots, digest verification and rollback paths. Treat every external archive/database as potentially hostile and run separate fuzzing/limits tests before declaring untrusted imports safe.

**Request work queue:** Current structures include bounded, serialized background storage processing. Capacity limits are not a universal rate limit or per-application permission system.

**Runtime profiles:** WebView2/Chrome browser stores and user databases are private machine state and must not be included in source-control snapshots, diagnostic bundles or release ZIPs.

### v2.1.0 targeted hardening source candidate

The latest source candidate adds explicit ConPTY session revocation when releasing a workspace, opt-in warnings for normal/elevated shells, safer `fs.list` enumeration, SQLite `trusted_schema = OFF` (with read-only `query_only = ON` during import), ZIP path/entry checks, and stricter manifest validation. These are **source changes awaiting Windows build and test execution**, not certified mitigations. The runtime still does not isolate native shell commands in a Windows process sandbox, and directory reparse-point races remain possible. See [the Turkish change and verification notes](docs/SECURITY_HARDENING_v2.1.0_TR.md).

### Hardening opportunities / required verification

The following remain essential review tasks before describing a build as "hardened" or "release-ready":

- Audit **which document origins and `.alek` identities can call `dev.*`, `safeAi.*`, `db.*` and `fs.*`**, with tests for hostile scripts and navigation races.
- Fuzz message parsing, unexpected/oversized JSON payloads, asynchronous close/disposal, and malformed `.alekdata`/ZIP inputs.
- Test all Windows path edge cases, reparse changes during operations, UNC/device syntax, 8.3 aliases, ADS and Unicode normalization.
- Validate media decoder exposure, file-size limits, decompression/ZIP-bomb handling, symlink import, extraction-to-temp hygiene and cleanup on failure.
- Verify sensitive values never enter WebView2 console logs, crash dumps or error dialogs, and review diagnostic retention.
- Test updater/release authenticity, Windows file association/registry behavior, uninstaller deletion boundaries and least privilege.
- Run real Windows x64 integration and clean-machine launch tests, including missing WebView2, restricted-profile and non-ASCII install paths.

## Repository and build supply chain

The following repository controls are supplied with this source package:

- **`.gitignore`** for .NET build output, Windows/IDE caches, WebView2 profiles, SQLite/user state, portable transfers, key/certificate material and local secrets.
- **`tools/Verify-SourceHygiene.ps1`** to detect known-sensitive paths/secret formats in the candidate source snapshot. It is deliberately heuristic and cannot guarantee detection of every secret.
- **`.github/workflows/security.yml`** to run hygiene and compile/check gates; CodeQL is an optional security workflow subject to the repository's GitHub plan/settings.
- **`.github/dependabot.yml`** to propose NuGet and GitHub Actions updates; merging any update requires testing.
- **`docs/SECURITY_CHECKLIST.md`** for maintainers' manual release sign-off.

### Mandatory repository practice

1. Before staging files, use `git status --short`, `git check-ignore -v <path>` and inspect `git diff --cached --name-only`.
2. Never rely on ignore rules to remove an already-tracked or previously committed secret. Use `git rm --cached`, **rotate/revoke compromised secrets immediately**, and assess whether Git-history rewriting and notification are required.
3. Never commit production user databases, browser profiles, private keys, signing assets, machine credentials or diagnostic dumps, including inside compressed archives.
4. Preserve *canonical* source images (`.png`, `.ico`, `.svg`), application assets, scripts and templates; they are build inputs, not garbage.
5. Apply least-privilege workflow permissions, review third-party Actions before adoption, and prefer immutable commit pins for critical workflows.
6. Protect the default branch, require successful build/tests and pull-request review where feasible, and enable secret scanning, Dependabot alerts, dependency review and CodeQL as the account plan permits.
7. Restore and test before publish. Publish the executable package separately from the source archive and include independently verifiable SHA-256 checksums and accurate release notes.
8. If signing is added, keep private signing keys outside Git and the build artifact; publish verification instructions and signing identity.

## User safety guidelines

- Download releases only from the maintainer's intended GitHub repository and inspect the corresponding release notes.
- Do not execute unknown `.alek` files or unknown shell commands; keep Windows Defender and the WebView2 runtime updated.
- Back up portable `Data/` and `Games/` folders before upgrades, importing archives or uninstalling a runtime.
- Keep private worlds, profile directories and terminal workspace paths out of bug reports.
- If a package claims v2.1.0 but its executable does not report v2.1.0 (or does not run), stop and verify provenance, build output and dependencies. Matching metadata alone does not prove a functioning upgrade.

## Scope and license

This policy covers Ałek’ryŧhæ Core security communication, not separate promises for every `.alek` Application, Chrome service, Windows component or third-party library. Source availability is governed by [TheDEvorger Universal Proprietary Software License v1.3](LICENSE), not an open-source license. Third-party components retain their respective licenses.

_Last documentation revision: 2026-10-10. Update release-support statements only after an actual Windows build and maintainer sign-off._
