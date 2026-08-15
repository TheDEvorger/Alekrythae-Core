# Changelog

## v0.1.2

### GPU selection and compatibility
- Added adapter-aware GPU selection for compatible `.alek` applications.
- On first use, Core can choose the strongest detected physical GPU automatically.
- A GPU selected manually by the user remains persistent across launches.
- Discrete/high-performance adapter selection also passes a high-performance GPU hint to WebView2/Chromium.
- Existing graphics bridge operations and legacy `preference`-based requests remain supported for older `.alek` applications.
- A temporarily unavailable adapter no longer causes the saved user selection to be discarded immediately.

### Compatibility
- The change is intentionally isolated to the graphics bridge.
- `.alek` loading, named pipes, SQLite/PortableGameStore, ExternalMediaBridge, DeveloperBridge, window behavior, suspend/resume, and existing file APIs retain their existing contracts.
- DeveloperBridge protocol revision is intentionally unchanged to avoid breaking clients that depend on the current bridge contract.

## v0.1.1

### Host exit reliability
- `app.exit` is intercepted directly in the WebView2 message handler, with or without a request id.
- The active `CosmicGate` window closes synchronously on the UI dispatcher instead of queueing `Close()` behind renderer work.
- If normal window closure fails, Core escalates to WPF application shutdown and finally a guarded process-exit fallback.
- The fallback is cancelled automatically as soon as `OnClosed` runs, so other open dimensions are not terminated after a successful close.

## v0.1.0

First public source release of Ałek’ryŧhæ Core.

### Included
- Windows `.alek` runtime/host source.
- WebView2 application hosting.
- Native C# bridge and runtime services.
- `.alek` file association and Windows integration.
- Current Meggy compatibility services retained for the first public release.
- Source package cleaned of user WebView profiles, cookies, sessions, caches, logs and application saves.

### Development status
The Core API and package/runtime conventions are not yet considered stable.
