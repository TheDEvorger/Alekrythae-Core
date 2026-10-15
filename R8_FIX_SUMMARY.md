# Ałek’ryŧhæ Core v2.0.0 R8

R8 is a black-screen/bridge observability hotfix on top of R7.

- Keeps the R7 abandoned-mutex and shutdown race fixes.
- Uses a fresh Revision 13 mutex/pipe identity so a resident R7 process cannot intercept R8 test launches.
- Enables WebView2 console/JavaScript exception logging in Release builds without enabling the DevTools UI.
- Adds a native `core.log` diagnostic channel for bootstrap errors.
- Adds a 15-second timeout to non-interactive Promise bridge calls, preventing `.alek` startup from hanging forever on a lost response.
- Interactive file/import/export bridge calls remain timeout-free.
- Logs app filename and generated boot HTML size before navigation.
- Logs bridge receive/response failures to `alekrythae_webview.log`.

If an app still renders a blank surface, inspect `alekrythae_webview.log`; R8 should now contain the actual JavaScript exception or bridge timeout instead of a silent black screen.
