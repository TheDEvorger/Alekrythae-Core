# Ałek’ryŧhæ Core v2.0.0 R7 Hotfix Summary

This source tree keeps the public version at v2.0.0 and advances the internal runtime revision from R6 to R7.

## Fixed

- Recovers an abandoned named mutex after a hard Core termination instead of crashing before the normal error logger is installed.
- Uses a new R7 single-instance mutex and named-pipe identity, so a stale/hung R6 process cannot block the repaired runtime.
- `.alek` file association is registered only by a direct Core EXE launch, not every time an application file is opened.
- Direct Core launches can refresh the Windows association even when another R7 Core process is already active.
- WebView2 startup is guarded against window-close races.
- WebView2 power-state callbacks stop once shutdown begins, avoiding disposed-object calls during teardown.
- Expected `OperationCanceled` navigation replacement is no longer logged as an application failure.
- Core shutdown clears the application lifetime marker before dispatcher shutdown, reducing the window where the pipe server can accept work while the host is exiting.
- Release build removes runtime cache/log artifacts and automatically creates a clean R7 ZIP before the output is opened for testing.

## First run after building

1. Run `BUILD_CORE.cmd`.
2. Use the generated `release\Alekrythae-Core-v2.0.0-Windows-x64-R7.zip` as the clean distributable.
3. Extract it to the final Core folder.
4. Run `Alekrythae Core.exe` once with no `.alek` argument. This registers that exact release path as the `.alek` handler.
5. Then test a known-good application such as JOA before testing Meggy.

## Important packaging rule

Do not launch the release folder and then ZIP that same live folder manually. Running Core creates runtime state such as `KozmikData` and logs. The build script now creates the clean ZIP before the release is opened for testing.
