# Isolated WebView2 software tests

This test-only executable reuses the production CosmicGate window/bridge. It does
not enter production Program.Main, create its shared mutex/pipe or register a tray
or file association. Production sources and the normal launch behavior are unchanged.

The launcher creates a uniquely named Windows desktop, then starts its own child
with STARTUPINFO.lpDesktop set to that name. It never switches the input desktop and
never requests SWITCHDESKTOP access. The child refuses to construct any WPF window
unless its actual thread desktop matches the generated test desktop. Once the HWND
exists, its owning thread's desktop is checked again. Each fixture has separate
browser profile/log directories. Failures have no visible-host fallback.

The child is assigned to a kill-on-close job while suspended, before it can create
WebView2 processes. Closing or killing the launcher closes the job and terminates
remaining descendants. Timed-out tests terminate only this job, never normal Core.

Build from the Core source root:

```powershell
dotnet build tests/Core.DesktopTests/Core.DesktopTests.csproj -c Release -m:1 --disable-build-servers
./tools/Test-NativeCore.ps1 -TestHostDirectory ./tests/Core.DesktopTests/bin/Release/net10.0-windows10.0.19041.0/win-x64 -Scenes
```

The runner requires host-policy.json, host-window.json and host-isolation.json.
A successful run must show a private window desktop, unchanged input desktop,
normal fixture results and a drained final write. Only synthetic
`test-output/native-<8 hex>/native-smoke.alek` files are accepted.

This isolates test windows. It does not establish screenshot-level artistic
acceptance, real clipboard gestures, long-session stability or ordinary desktop
performance. Private-desktop timings must not be compared with foreground runs as
whole-application performance measurements. The optional Canvas2D comparison
covers command submission and matched pixels within one run only.

Windows behavior was checked against Microsoft's documentation:
[CreateDesktopW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createdesktopw),
[thread desktop assignment](https://learn.microsoft.com/en-us/windows/win32/winstation/thread-connection-to-a-desktop),
and [STARTUPINFO.lpDesktop](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow).

## Embedded previews and populated session

The same runner also accepts `-F1` (32 empty/populated/door-link viewport cases),
`-Previews` (eight PNGs of the fixture's own WebView2 via CapturePreviewAsync),
and `-Soak -TimeoutSeconds 360` (three 120-trip phases and a fourth verification
process using the same fixture database/profile). No OS desktop pixels or user
clipboard are captured. These scene fixtures explicitly bypass the opening screen;
its animation and ordinary foreground transition acceptance are outside this scope.

The populated fixture has six linked scene canvases, 83 placements including a
vehicle carrying ten passengers, 64 seeded strokes and 48 image placements sharing
three PNG paths per scene. Each new process compares the saved graph and all six
SQLite art documents before continuing. F1/F3/map navigation and map-cache disposal
repeat during each phase. `phase-*-native-result.json` and desktop evidence keep
per-process results separate; a failure stops the sequence.

`host-metrics.json` samples only the fixture host and its own WebView2 environment
at five-second intervals. CPU milliseconds, working sets and private bytes are
process counters; shared working-set pages can appear in multiple processes.
GPU-process counters do not measure GPU utilization. Private-desktop measurements
are not comparable foreground FPS or a performance improvement claim. The runner
allows a 30-900 second timeout per process; the default remains 75 seconds.


`-Performance -TimeoutSeconds 240` adds paired static-canvas repaint/pixel checks.
MetricsRequest responds at explicit phase boundaries with SessionMetrics process
counters from this fixture's own environment. Private callback intervals and GPU
process CPU/memory do not establish foreground FPS or GPU utilization. Boot a
prepared JS package with `-AppDirectory <repository/release/package> -Joa`; the
package itself remains untouched and only its disposable fixture copy is opened.


-Scenes also exercises F1 root/child buttons, normal new doors and reciprocal F1
targeting, brush/image edits, exact preview/commit/failure/return pixels, and a
delayed target with a duplicate travel request. It adds two edit/return trips to
the six existing vehicle/passenger trips. scene-create-probe.js records whether
Core exposes randomUUID. Direct synthetic drag methods bypass pointer capture
only during that call; no active OS pointer or trusted input is claimed. Failed
save injection wraps the frozen bridge without changing its production object.

## Opening without bypass

Run `-Opening -TimeoutSeconds 100` separately from other view fixtures, optionally
with `-AppDirectory <release/package>`. It uses the real opening and portal handlers,
animation durations, saved time seal and map-to-portal return. Synthetic DOM Enter
and click events activate those handlers; no splash removal or manual unlocking is
allowed. Eleven embedded PNGs cover idle/draining, wide/narrow/short portal, time
seal, interface and scene/portal return. Geometry assertions require visible card
area and horizontally fitting cards, plus a visible adventure title at 568x320.

This remains a private-desktop test. Captured images and handler assertions do not
establish trusted foreground input, OS clipboard gestures or foreground FPS/GPU
utilization. Only the fixture copy receives the test accessors; app payloads stay
free of tests and synthetic saves. Current progress and run evidence are in the
repository TASKS.md.

## Clipboard compatibility

`-Joa` records whether the actual Core document exposes async clipboard writing
and the browser copy command; it does not read or write the OS clipboard.
`-Scenes` also exercises mounted Tavern/F3 10/100/1000 controls with 1100 Unicode
messages and the journal's saved gameplay events. The temporary selected copy field,
exact payload, restored focus/input selection, false/throw failure and retry are
checked. Only document.execCommand is intercepted inside the disposable fixture.
The production async/fallback choice is retained; no browser capability is faked.

Command interception does not prove OS clipboard delivery or trusted input. Keep
the foreground copy/paste acceptance open until actual Windows gestures can run.
