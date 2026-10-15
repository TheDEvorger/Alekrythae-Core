// Preview capture of the test fixture's own embedded WebView2; no OS desktop capture.
(async function scenePreviews() {
    const previews = [];
    const waitFor = async (predicate) => {
        for (let turn = 0; turn < 200; turn++) {
            if (predicate()) return;
            await new Promise((resolve) => setTimeout(resolve, 50));
        }
        throw new Error("Preview request timed out.");
    };
    const capture = async (name, width, height) => {
        window.__nativePreviewRequest = { name, width, height };
        await waitFor(() => window.__nativePreviewResult?.name === name);
        const result = window.__nativePreviewResult;
        if (!result.ok) throw new Error(result.error);
        const expected = name.startsWith("f1-")
            ? ".joa-scene-f1"
            : name.startsWith("map-")
              ? ".joa-scene-surface"
              : name.startsWith("tavern-")
                ? ".tavern-stage"
                : ".joa-event-log";
        const center = document.elementFromPoint(innerWidth / 2, innerHeight / 2);
        if (!center?.closest(expected))
            throw new Error("Preview covered by another screen: " + name + "/" + center?.className);
        previews.push({ ...result, cssWidth: innerWidth, cssHeight: innerHeight, devicePixelRatio });
    };
    try {
        await window.__ALEK_BOOT_PROMISE__;
        const access = window.__nativeSceneAcceptance;
        await access.setup();
        let root = await access.f1(false, false);
        await waitFor(() => root?.querySelector(".joa-scene-f1-main"));
        await capture("f1-empty-wide.png", 1536, 864);
        root = await access.f1(true, false, { depth: 0, occupants: 3 });
        await waitFor(() => root?.querySelectorAll(".joa-scene-f1-occupant").length === 3);
        await capture("f1-populated-wide.png", 1536, 864);
        await capture("f1-populated-narrow.png", 320, 568);
        await capture("f1-populated-short.png", 568, 320);
        root = await access.map();
        await waitFor(() => root.classList.contains("scene-surface-active"));
        await capture("map-three-actors-wide.png", 1536, 864);
        root.querySelector(".joa-scene-tools").focus();
        await capture("map-palette-wide.png", 1536, 864);
        await capture("map-palette-narrow.png", 320, 568);
        root = await access.render("encounter", true);
        await waitFor(() => root.querySelector(".tavern-composer-shell"));
        await capture("tavern-wide.png", 1536, 864);
        await capture("tavern-narrow.png", 512, 384);
        root = await access.render("ai", true);
        await waitFor(() => root.querySelector(".joa-event-log-list"));
        await capture("journal-wide.png", 1536, 864);
        await capture("journal-narrow.png", 512, 384);
        await window.__alekWriteJson("native-result.json", {
            ok: true,
            kind: "scene-ui-previews",
            introBypassed: true,
            previews,
        });
    } catch (error) {
        await window.__alekWriteJson("native-result.json", {
            ok: false,
            error: String(error.stack || error),
            previews,
            diagnostic: {
                owner: window.__ALEK_ORCHESTRATOR__
                    .getModule("joa.world-experience")
                    .controller.bridge.getAdventureSession(),
                opening: window.__ALEK_COSMIC_OPENING_LOCKED__,
                f1: !!document.querySelector(".joa-scene-f1"),
                request: window.__nativePreviewRequest,
                reply: window.__nativePreviewResult,
                text: document.querySelector(".joa-scene-f1")?.textContent.slice(0, 500),
            },
        });
    }
    window.__alekWriteText("tail.txt", "drained before exit");
    window.__alekAPI("app.exit", {});
})();
