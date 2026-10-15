// Appended only to an isolated copy of the real JOA entry for native integration testing.
(async function probeJoaBoot() {
    let timeout;
    try {
        await Promise.race([
            window.__ALEK_BOOT_PROMISE__,
            new Promise((_, reject) => {
                timeout = setTimeout(() => reject(new Error("JOA boot timeout")), 60000);
            }),
        ]);
        clearTimeout(timeout);
        const app = window.__ALEK_ORCHESTRATOR__;
        if (app?.status?.phase !== "running") throw new Error(window.__ALEK_BOOT_ERROR__ || "JOA not running");
        const modules = app.listModules();
        if (!modules.length || modules.some((module) => module.state !== "started"))
            throw new Error("JOA module startup incomplete");
        await window.__alekWriteJson("native-result.json", {
            ok: true,
            phase: app.status.phase,
            modules,
            clipboardCapability: {
                secureContext: window.isSecureContext,
                writeTextAvailable: typeof navigator.clipboard?.writeText === "function",
                execCommandAvailable: typeof document.execCommand === "function",
                clipboardRead: false,
                clipboardWritten: false,
            },
        });
        window.__alekWriteText("tail.txt", "drained before exit");
        window.__alekAPI("app.exit", {});
    } catch (error) {
        clearTimeout(timeout);
        await window.__alekWriteJson("native-result.json", { ok: false, error: String(error.stack || error) });
        window.__alekAPI("app.exit", {});
    }
})();
