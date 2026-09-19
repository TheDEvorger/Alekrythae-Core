<div align="center">

# Ałek’ryŧhæ Core

### Portable Windows runtime for `.alek` dimensions

**Ałek’ryŧhæ Core** is the Windows runtime layer behind the Ałek’ryŧhæ ecosystem: a self-contained host for `.alek` applications with portable data storage, sandboxed file access, GPU selection, media streaming, developer tooling, and version-safe data transfer.

[![Version](https://img.shields.io/badge/version-v2.0.0-7c3aed?style=for-the-badge)](#release-status)
[![Core](https://img.shields.io/badge/core-R6-2563eb?style=for-the-badge)](#release-status)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=for-the-badge&logo=windows11&logoColor=white)](#requirements)
[![SQLite](https://img.shields.io/badge/storage-SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white)](#portable-data-layer)
[![Status](https://img.shields.io/badge/status-active%20development-16a34a?style=for-the-badge)](#roadmap)

> **Current release:** `v2.0.0` · **Core revision:** `R6` · **Target:** `Windows x64`

</div>

---

## Overview

Ałek’ryŧhæ Core turns a `.alek` file into a desktop application surface while keeping its runtime data portable and close to the application itself.

The Core provides the native Windows-side services that an `.alek` application can call through a controlled bridge: filesystem operations, SQLite-backed game data, graphics preference management, external media access, import/export, developer workspace access, terminal sessions, and application lifecycle control.

The project is intentionally built around one rule:

> **Application content stays portable; native capabilities stay behind explicit Core APIs.**

---

## Highlights

| Capability | What it provides |
| --- | --- |
| 🌀 **`.alek` runtime** | Registers and launches `.alek` dimensions directly from Windows. |
| 💾 **Portable SQLite storage** | Keeps registry and adventure data beside the `.alek` root instead of scattering game data through the user profile. |
| 🧱 **Sandboxed filesystem bridge** | File operations are constrained to the active `.alek` root. |
| ⚛️ **Atomic writes** | Text, JSON, binary, and memory writes use same-directory temporary files and atomic replacement to reduce partial-write risk. |
| 🎮 **GPU preference bridge** | Detects graphics adapters and persists the selected Windows GPU preference. |
| 🎞️ **External media bridge** | Imports and streams supported audio, video, and image formats without permanently storing arbitrary absolute source paths. |
| 📦 **Portable data transfer** | Exports/imports `.alekdata` packages with SQLite snapshots, SHA-256 verification, compatibility handling, and rollback protection. |
| 🛠️ **Developer bridge** | Workspace file access plus a Windows ConPTY-backed terminal for development workflows. |
| 🤖 **AI workspace dock** | Can visually dock a separate Chrome application-mode window into the AI workspace without reading browser DOM, cookies, passwords, or session tokens. |
| 💤 **Runtime power management** | Reduces WebView2 memory/power pressure while the application is inactive. |
| 🔁 **Backward compatibility** | R6 preserves the existing SQLite schema and established Core API namespaces used by earlier compatible data. |
| 🌙 **ViodCera bridge** | Adds resident hotkeys, no-activate translation popups, live OCR region selection, local Windows OCR, and ViodCera window lifecycle APIs. |

---

## Architecture

```text
                         ┌──────────────────────┐
                         │      .alek app       │
                         │  UI + application JS │
                         └──────────┬───────────┘
                                    │
                            Core message bridge
                                    │
              ┌─────────────────────┴─────────────────────┐
              │            Ałek’ryŧhæ Core R6             │
              │                .NET 10 / WPF               │
              └─────────────────────┬─────────────────────┘
                                    │
       ┌─────────────┬──────────────┼──────────────┬──────────────┐
       │             │              │              │              │
       ▼             ▼              ▼              ▼              ▼
  Filesystem      SQLite       GPU / Graphics   Media        Developer
   fs.* API       db.* API        Bridge         Bridge         Bridge
       │             │              │              │              │
       ▼             ▼              ▼              ▼              ▼
 Sandbox root   Data/ + Games/  Windows GPU    Media files   Workspace +
                                preferences                   ConPTY shell
```

### Core components

```text
src/Alekrythae.Core/
├── Program.cs                  # Process lifecycle, .alek association, startup
├── CosmicGate.cs               # Main runtime window + WebView2/Core API bridge
├── ViodCeraBridge.cs           # ViodCera hotkeys, OCR, translation popup + resident bridge
├── PortableGameStore.cs        # Portable SQLite persistence layer
├── DataTransferService.cs      # .alekdata import/export and migration
├── GraphicsBridge.cs           # GPU discovery and Windows graphics preference
├── ExternalMediaBridge.cs      # Approved media import/streaming
├── DeveloperBridge.cs          # Developer workspace and shell APIs
├── ConPtySession.cs            # Native Windows pseudo-terminal session
├── EdgeChatGptDock.cs          # External Chrome AI workspace docking
├── CoreUninstaller.cs          # Core cleanup/uninstall workflow
└── Resources/                  # Runtime icons, artwork, cursor extension
```

---

## Portable Data Layer

Ałek’ryŧhæ Core is designed to keep application data portable.

```text
<alek-root>/
├── Data/
│   └── meggy.db
├── Games/
│   └── <adventure>/
│       ├── game.db
│       └── Media/
└── <dimension>.alek
```

`PortableGameStore` deliberately avoids writing game data into `AppData`, `Documents`, temporary folders, or other user-profile locations.

The Core currently exposes registry/game operations through the `db.*` namespace, including registry reads/writes, game creation, document reads/writes, existence checks, deletion, and rename operations.

---

## Data Safety & Transfer

R6 hardens write and transfer paths against corruption and unsafe imports.

### Atomic filesystem writes

`fs.writeText`, `fs.writeJson`, `fs.writeBinary`, and runtime memory writes use a unique temporary file in the destination directory followed by atomic replacement. Failed operations attempt to clean up their temporary files.

### `.alekdata` packages

The transfer layer can move user data between compatible installations while protecting the live store:

- SQLite databases are exported from consistent snapshots rather than by blindly copying a live database file.
- Package files are verified with **SHA-256**.
- Import does not overwrite a conflicting adventure in place; conflicts are handled as separate copies.
- An automatic rollback package is created before import.
- Legacy JSON, SQLite, and older Meggy ZIP-style sources can be recognized by the migration layer.

---

## Graphics

`GraphicsBridge` provides Windows-side graphics adapter discovery and preference management.

Supported Core operations include:

```text
listGraphicsAdapters
setGraphicsPreference
getGraphicsPreference
openWindowsGraphicsSettings
```

The persisted preference is also used to build the WebView2 browser arguments used by the runtime.

---

## External Media

The media bridge supports controlled import/probing of common formats, including:

```text
Audio : MP3, M4A, AAC, WAV, OGG, OPUS, FLAC, WMA
Video : MP4, WEBM, MOV
Image : PNG, JPG/JPEG, WEBP, GIF, BMP, SVG, AVIF
```

Selected external paths are approved only for the import flow. Imported media is copied into the active adventure's `Media` directory; arbitrary original absolute paths are not intended to become permanent game-state references.

---

## Developer Bridge

R6 includes a dedicated `dev.*` API surface for tooling without changing the established application APIs.

```text
dev.status
dev.workspace.pick
dev.workspace.release
dev.workspace.list
dev.workspace.readText
dev.workspace.writeText
dev.workspace.exists
dev.workspace.mkdir
dev.shell.start
dev.shell.write
dev.shell.resize
dev.shell.stop
```

Terminal sessions are backed by **Windows ConPTY**, use UTF-8 streams, and support Unicode Windows paths.

---

## AI Workspace Dock

The Core can dock a separate **Google Chrome application-mode window** into the application's AI workspace.

The browser remains a separate process with its own profile. The docking layer is designed for window positioning and lifecycle integration; it does **not** read the page DOM, cookies, passwords, network traffic, or browser session keys.

---

## ViodCera Runtime Support

Core `v2.0.0` includes the ViodCera native bridge required by `Alekrythae-ViodCera.alek`. The bridge is activated only for the ViodCera application identity/file name and keeps normal `.alek` behavior unchanged.

```text
Ctrl + Q       selected-text translation
Alt + Q        live OCR selection
Space / Enter  confirm OCR region
Esc            cancel OCR selection
Ctrl + Wheel   application font scaling
```

The ViodCera host can remain resident while hidden so WebView2 stays warm for fast popup use. OCR selection is transparent and does not intentionally dim or freeze the screen.

---

## Requirements

### Build environment

- **Windows 10 version 2004 or later**
- **Windows x64**
- **.NET 10 SDK**
- **Microsoft Edge WebView2 Runtime**

Google Chrome is only needed for the optional external AI workspace docking feature.

### Main dependencies

| Package | Version |
| --- | ---: |
| `Microsoft.Data.Sqlite` | `10.0.10` |
| `SQLitePCLRaw.bundle_e_sqlite3` | `2.1.12` |
| `Microsoft.Web.WebView2` | `1.0.3912.50` |

---

## Build

Clone the repository and run the supplied build script from the repository root:

```bat
BUILD_CORE.cmd
```

The script restores dependencies and publishes a self-contained Windows x64 release to:

```text
release/Alekrythae-Core-v2.0.0-Windows-x64/
```

Expected output includes:

```text
Alekrythae Core.exe
Alekrythae Core.runtimeconfig.json
Alekrythae Core.deps.json
```

For the source-level performance patch verification followed by a normal build:

```bat
BUILD_CORE_PERF.cmd
```

---

## Run

Ałek’ryŧhæ Core associates itself with the `.alek` extension for the current Windows user.

The normal runtime flow is simply:

```text
Double-click a .alek file
        ↓
Ałek’ryŧhæ Core
        ↓
Dimension opens in its isolated root
```

Launching the Core without a `.alek` argument performs its registration work and exits rather than remaining idle in the background.

---

## Core API Surface

R6 preserves the established API families used by compatible `.alek` applications:

```text
fs.*
db.*
data.*
Graphics / GPU operations
External media operations
AI operations
dev.*
app.exit
```


---

## Release Status

### `v2.0.0` · R6

R6 is the current Core revision shipped with `v2.0.0`.

Key characteristics include:

- hardened shortcut blocking while modal/character/palette windows are active;
- unique temporary files plus atomic replacement for write operations;
- cleanup of failed temporary writes;
- synchronized `v2.0.0` version metadata across the program, project, developer bridge, compatibility metadata, and build output;
- stable SQLite-backed portable storage and established bridge API namespaces.

---

## Repository Layout

```text
.
├── src/
│   └── Alekrythae.Core/        # Core source code
├── Alekrythae.sln              # Visual Studio solution
├── BUILD_CORE.cmd              # Release build
├── BUILD_CORE_PERF.cmd         # Performance patch check + release build
├── VERSION                     # Current SemVer version
└── .gitignore                  # Repository hygiene / runtime-data exclusions
```

Runtime-generated data, databases, caches, logs, backups, and local environment secrets are intentionally excluded from Git by the repository `.gitignore`.

---

## Versioning

The project separates the public release version from the Core architecture revision:

```text
Release version : v2.0.0
Core revision   : R6
Display version : Ałek’ryŧhæ Core v2.0.0 (R6)
```

Release tags follow **Semantic Versioning** style (`vMAJOR.MINOR.PATCH`), while `R#` identifies the internal Core revision line.

---

## Roadmap

Ałek’ryŧhæ Core is under active development. Current engineering priorities include runtime hardening, portability, graphics efficiency, developer tooling, and continued evolution of the application/runtime boundary without breaking existing user data.

---

<div align="center">

### Ałek’ryŧhæ Core · Build worlds. Keep the runtime under control.

`v2.0.0` · `R6` · `.NET 10` · `Windows x64`

</div>