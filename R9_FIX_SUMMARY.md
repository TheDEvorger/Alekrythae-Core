# Ałek’ryŧhæ Core v2.0.0 R9

R9 fixes the black-screen regression identified by the R8 diagnostic log.

## Root cause

`WebView2.NavigateToString(...)` can surface the generated application document to `NavigationStarting` as a `data:text/html;charset=utf-8;base64,...` top-level navigation. R8's browser-hardening handler allowed only `about:blank` and cancelled every other top-level navigation. The Core therefore cancelled its own generated `.alek` document before the application JavaScript could run.

The characteristic R8 log was:

```text
boot: ...
boot html: ...
blocked top-level navigation: data:text/html;charset=utf-8;base64,...
```

## Fix

- `FireUpJs` arms a short-lived, single-use permission immediately before `NavigateToString`.
- `OnNavigationStarting` accepts exactly one generated `data:text/html` navigation while that permission is armed.
- The permission is consumed immediately and expires after 10 seconds.
- Any later app-driven `data:` navigation remains blocked.
- R9 uses Revision 14 mutex/pipe identities so a resident R8 process cannot intercept R9 test launches.
- R8 bridge timeouts and Release diagnostics remain enabled.

## Expected log after the fix

```text
boot: Alekrythae.alek / js=... chars
boot html: ... chars
allowed generated document navigation
```

After that, normal bridge/application log entries should follow.
