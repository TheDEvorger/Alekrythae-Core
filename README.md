# Ałek’ryŧhæ Core

> **The shared runtime and host for `.alek` applications.**

Ałek’ryŧhæ Core is a Windows application runtime built with **C# / .NET** and **Microsoft WebView2**.  
Its purpose is to provide one common host for applications packaged in the **`.alek`** format, while keeping the runtime itself separate from the applications that use it.

The project follows a simple idea:

**Core handles the runtime. `.alek` applications handle the product.**

An `.alek` application can therefore evolve independently while reusing the same Core services, application loading pipeline, local data infrastructure, window/runtime behavior, and WebView2 integration.

---

## What is Ałek’ryŧhæ Core?

Ałek’ryŧhæ Core is not a single-purpose application.

It is the shared runtime layer of the Ałek’ryŧhæ application architecture.

The Core is responsible for tasks such as:

- opening and validating `.alek` application packages;
- reading application manifests and resolving entry points;
- hosting WebView2-based applications;
- providing a bridge between JavaScript and native C# functionality;
- managing application-local runtime paths and resources;
- providing shared local-storage/database services where supported;
- handling application lifecycle and startup behavior;
- providing common native capabilities to compatible `.alek` applications;
- acting as the dispatcher between different `.alek` application types.

The long-term architecture is intentionally **technology-independent at the package level**.

A `.alek` package is not defined as “a JavaScript app” or “a C# app”.  
The package describes an application, while the Core decides how that application should be launched.

This allows the architecture to support:

- **WebView2 / JavaScript applications**, using the existing web runtime path;
- **native C# applications**, where a compatible native entry point can be launched without requiring a WebView2 application surface.

---

## Why separate the Core from the applications?

Keeping the runtime separate gives the project a cleaner architecture.

Instead of every application carrying its own copy of the same hosting and native-integration code:

```text
Ałek’ryŧhæ Core
        │
        ├── Application A.alek
        ├── Application B.alek
        ├── Application C.alek
        └── ...
```

The Core becomes the common execution environment.

This separation makes it possible to:

- update the Core independently;
- version applications independently;
- keep application repositories smaller and cleaner;
- avoid duplicating runtime code;
- share native services between multiple applications;
- keep user-facing applications focused on their own features;
- evolve the `.alek` ecosystem without turning every application into a monolithic executable.

---

## The `.alek` application model

A compatible `.alek` application is expected to contain the files and metadata required by the application itself.

A package may include, depending on the application:

```text
Application.alek
├── manifest
├── application modules
├── scripts
├── UI resources
├── images / audio / other assets
├── schemas / metadata
└── application-specific configuration
```

The exact contents can vary by application.

The important architectural rule is that **the application package and the Core are separate components**.

The Core provides the runtime.  
The `.alek` package provides the application.

---

## Runtime flow

A simplified startup flow looks like this:

```text
User opens an .alek application
            │
            ▼
    Ałek’ryŧhæ Core
            │
            ▼
  Read / validate manifest
            │
            ▼
 Determine application type
        ┌───┴───────────┐
        ▼               ▼
 WebView2 / JS       Native C#
 application         application
        │               │
        └───────┬───────┘
                ▼
       Shared Core services
```

The runtime is designed so that application-specific logic stays inside the application rather than being hard-coded into the Core.

---

## WebView2 integration

For web-based `.alek` applications, Ałek’ryŧhæ Core uses **Microsoft Edge WebView2** as the application surface.

This makes it possible to build rich desktop interfaces using familiar web technologies while still having access to native C# functionality through the Core bridge.

Typical application-side technologies can include:

- HTML
- CSS
- JavaScript
- modular JavaScript application code
- browser-native rendering
- local media and assets

The Core remains responsible for the native host.

---

## Native bridge

A major responsibility of the Core is communication between the hosted application and native Windows functionality.

Conceptually:

```text
JavaScript Application
        │
        │ request
        ▼
   Core Bridge
        │
        ▼
      C# / .NET
        │
        │ result
        ▼
JavaScript Application
```

This allows compatible applications to request supported native operations without embedding all native implementation details into the application itself.

The bridge is intentionally part of the Core so shared native functionality can remain centralized.

---

## Local-first design

Ałek’ryŧhæ applications are designed around a **local-first desktop workflow**.

Application data can be stored locally through the storage mechanisms provided or supported by the application and Core architecture.

The public source/release packages in this repository must not contain developer/user runtime data such as:

- personal application saves;
- application databases containing user records;
- browser sessions;
- WebView2 user profiles;
- cookies;
- authentication sessions;
- browser history;
- cache data;
- personal logs;
- private application media.

Runtime-generated user data belongs on the user's machine, not in the repository.

---

## Project structure

The exact structure may evolve, but the Core source is organized around runtime responsibilities rather than application-specific features.

Typical areas include:

```text
Core
├── application/package loading
├── manifest handling
├── runtime dispatch
├── WebView2 hosting
├── native bridge
├── local services
├── lifecycle management
├── resource/runtime management
└── Windows integration
```

Application-specific interfaces, game systems, editors, calendars, notes, productivity features, and similar functionality belong in their respective `.alek` application repositories.

---

## Example application: Ałek’ryŧhæ Meggy

**Ałek’ryŧhæ Meggy** is one application built for this architecture.

Meggy is maintained separately from the Core and is distributed as its own `.alek` application project.

This is intentional:

- **Core repository:** runtime, hosting, native infrastructure.
- **Meggy repository:** Meggy's UI, workflows, assets, and application-specific behavior.

The same Core can be used as the host for other compatible `.alek` applications.

---

## Requirements

For the Windows runtime:

- Windows 10/11
- compatible .NET runtime / SDK for building from source
- Microsoft Edge WebView2 Runtime
- Visual Studio or the .NET CLI for development

Exact framework requirements should be taken from the project files in the current release.

---

## Building from source

Clone the repository:

```bash
git clone https://github.com/TheDEvorger/Alekrythae-Core.git
cd Alekrythae-Core
```

Then restore and build using the .NET CLI:

```bash
dotnet restore
dotnet build
```

For a release build:

```bash
dotnet publish -c Release
```

The precise publish command can vary depending on the target framework and runtime identifier used by the current project configuration.

---

## Installing / using the Core

A release build of the Core can be installed or placed on the local Windows system and associated with the `.alek` file type.

Once the association is configured, a compatible `.alek` application can be opened through Ałek’ryŧhæ Core.

Conceptually:

```text
Double-click Application.alek
          ↓
Windows .alek association
          ↓
Ałek’ryŧhæ Core
          ↓
Application starts
```

Applications may require a specific Core version. Compatibility between every historical Core and application version is not guaranteed.

---

## Development principles

The project aims to keep the runtime:

- **small in responsibility** — application features belong to applications;
- **modular** — shared services should not become one giant class;
- **local-first** — user data remains local unless an application explicitly implements something else;
- **practical** — architecture exists to make product development easier, not to create ceremony;
- **extensible** — new `.alek` application types should be supportable without rewriting the entire runtime;
- **performance-aware** — inactive application surfaces and unnecessary background work should be minimized;
- **cleanly separated** — Core code and application code should not slowly collapse back into one monolith.

---

## Current status

Ałek’ryŧhæ Core is under active development.

APIs, manifests, runtime behavior, package conventions, bridge operations, compatibility rules, and internal architecture may change between versions.

Do not assume backward or forward compatibility unless a release explicitly states it.

---

## Security and privacy

Public repository and release packages should contain **no developer authentication state or personal runtime profile**.

In particular, WebView2 runtime profiles such as cookies, sessions, local storage, history, cached credentials, and browser cache are runtime-generated data and are not intended to be committed to this repository.

If you build or package the Core yourself, review generated runtime folders before publishing a release.

---

## License

Ałek’ryŧhæ Core is **source-available proprietary software**.

It is **not open-source software** and is not distributed under MIT, Apache, GPL, BSD, or another OSI-approved open-source license.

The project is licensed under:

**THEDEVORGER UNIVERSAL PROPRIETARY SOFTWARE LICENSE — Version 1.3**  
SPDX identifier: `LicenseRef-TheDevorger-UPSL-1.3`

See [`LICENSE.md`](LICENSE.md) for the complete terms.

Source availability does not automatically grant rights to redistribute, commercialize, create derivative products, build alternative `.alek` runtimes, or use the project for AI/ML training.

---

## Third-party components

Ałek’ryŧhæ Core may depend on third-party technologies such as .NET and Microsoft Edge WebView2.

Third-party components remain subject to their respective licenses.

See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) where applicable.

---

## Contributions

Issues and technical feedback may be useful during development.

Before submitting code or other material, read the contribution provisions in the project license. Contributions may be subject to specific licensing terms.

---

## Repository language

The primary README is written in English.

For the Turkish version:

**[README_TR.md](README_TR.md)**

---

## Author

**TheDevorger**

Licensing / legal contact:  
`thedevorger.alekrythae.dev@gmail.com`

---

<p align="center">
  <strong>Ałek’ryŧhæ Core</strong><br>
  One runtime. Multiple applications. One `.alek` architecture.
</p>
