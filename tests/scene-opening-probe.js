// Real opening handlers and animation clocks; synthetic DOM input on a private desktop.
(async function openingAcceptance() {
    const previews = [],
        checks = [];
    const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
    const assert = (value, message) => {
        if (!value) throw new Error(message);
        checks.push(message);
    };
    const waitFor = async (predicate, label) => {
        const deadline = performance.now() + 20000;
        while (performance.now() < deadline) {
            if (predicate()) return;
            await pause(50);
        }
        throw new Error("Timed out: " + label);
    };
    const capture = async (name, width = 1280, height = 720) => {
        window.__nativePreviewRequest = { name, width, height };
        await waitFor(() => window.__nativePreviewResult?.name === name, name);
        const result = window.__nativePreviewResult;
        assert(result.ok, "Captured " + name);
        previews.push({ ...result, cssWidth: innerWidth, cssHeight: innerHeight, devicePixelRatio });
    };
    const key = (node, value) =>
        node.dispatchEvent(
            new KeyboardEvent("keydown", {
                key: value,
                bubbles: true,
                cancelable: true,
            }),
        );
    const visibleImage = (node) => {
        const image = node?.querySelector("img");
        return image?.complete && image.naturalWidth > 0;
    };
    const portalGeometry = (portal, compact = false) => {
        const grid = portal.querySelector(".adventure-portal-grid");
        const card = grid.querySelector(".portal-adventure-card");
        const bounds = grid.getBoundingClientRect(),
            rect = card.getBoundingClientRect();
        assert(bounds.height >= 140, "Portal retains usable card area at " + innerWidth + "x" + innerHeight);
        assert(
            rect.left >= bounds.left && rect.right <= bounds.right,
            "Portal card fits horizontally at " + innerWidth,
        );
        if (compact) {
            const title = card.querySelector(".adventure-card-title").getBoundingClientRect();
            assert(
                title.top >= bounds.top && title.bottom <= bounds.bottom,
                "Short portal exposes the adventure title",
            );
        }
    };
    try {
        await window.__ALEK_BOOT_PROMISE__;
        const access = window.__nativeOpeningAcceptance;
        await waitFor(() => document.querySelector(".alek-opening-moon"), "opening mount");
        const id = await access.seed();
        const splash = document.querySelector(".alek-cosmic-opening");
        const moon = splash.querySelector(".alek-opening-moon");
        await waitFor(() => visibleImage(moon), "opening moon image");
        assert(access.snapshot().locked === true, "Opening remains locked before activation");
        assert(!access.snapshot().mapMounted, "No map mounted behind opening");
        assert(splash.querySelector(".alek-opening-spark-canvas"), "Opening spark canvas mounted");
        await capture("opening-idle.png");
        key(document, "Escape");
        await pause(100);
        assert(
            splash.isConnected && !splash.classList.contains("alek-opening-draining"),
            "Escape cannot bypass opening",
        );
        const started = performance.now();
        key(moon, "Enter");
        assert(
            moon.disabled && splash.classList.contains("alek-opening-draining"),
            "Activation starts locked spark drain",
        );
        key(moon, "Enter");
        assert(
            document.querySelectorAll(".adventure-portal-shade").length === 1,
            "Duplicate activation creates one portal",
        );
        await capture("opening-draining.png");
        assert(splash.isConnected, "Source opening remains during drain");
        await waitFor(() => !splash.isConnected, "opening flight and fade");
        const openingMs = performance.now() - started;
        assert(openingMs >= 2400, "Opening respects the 2400ms drain minimum");
        const portal = document.querySelector(".adventure-portal-shade");
        assert(portal && access.snapshot().locked === true, "Adventure choice remains gated after opening flight");
        await waitFor(() => visibleImage(portal.querySelector(".adventure-portal-moon")), "portal moon image");
        await capture("opening-portal-wide.png");
        portalGeometry(portal);
        await capture("opening-portal-narrow.png", 320, 568);
        portalGeometry(portal);
        await capture("opening-portal-short.png", 568, 320);
        portalGeometry(portal, true);
        await capture("opening-portal-restored.png");
        const card = portal.querySelector('[data-adventure-id="' + id + '"]');
        assert(card && !card.disabled, "Synthetic scene adventure is selectable");
        card.click();
        await waitFor(() => portal.dataset.entering === "true", "card activation");
        assert(
            [...portal.querySelectorAll("button")].every((button) => button.disabled),
            "Portal buttons lock during entry",
        );
        await waitFor(() => document.querySelector(".adventure-start-time-modal"), "time seal after entry");
        assert(
            !portal.isConnected && !document.querySelector(".alek-cosmic-opening"),
            "Time seal opens after portal and opening close",
        );
        assert(
            access.snapshot().locked === false && access.snapshot().activeId === id,
            "Entry unlocks only the selected adventure",
        );
        await capture("opening-time-seal.png");
        document.querySelector(".adventure-start-time-modal .primary").click();
        await waitFor(() => !document.querySelector(".adventure-start-time-modal"), "time seal commit");
        await waitFor(() => access.snapshot().startConfigured === true, "saved start time");
        assert(access.snapshot().sessionReady, "Scene adventure session is ready");
        await capture("opening-interface.png");
        await access.map();
        await waitFor(() => document.querySelector(".joa-scene-canvas"), "scene canvas mount");
        await capture("opening-scene.png");
        access.moon().click();
        await waitFor(() => document.querySelector(".adventure-portal-shade"), "return portal");
        await pause(1500);
        assert(!access.snapshot().mapMounted, "Returning to portal releases the scene surface");
        await capture("opening-return-portal.png");
        document.querySelector('[data-adventure-id="' + id + '"]').click();
        await waitFor(() => !document.querySelector(".adventure-portal-shade"), "return entry");
        await waitFor(() => document.querySelector(".joa-scene-canvas"), "restored scene surface");
        assert(!document.querySelector(".adventure-start-time-modal"), "Saved time seal is not requested again");
        assert(
            access.snapshot().activeId === id && access.snapshot().sessionReady,
            "Return preserves the adventure session",
        );
        await capture("opening-return-scene.png");
        await window.__alekWriteJson("native-result.json", {
            ok: true,
            kind: "scene-opening",
            introBypassed: false,
            trustedInput: false,
            foregroundAcceptance: false,
            osClipboardTested: false,
            foregroundPerformance: false,
            gpuUtilizationMeasured: false,
            openingMs,
            checks,
            previews,
            final: access.snapshot(),
        });
    } catch (error) {
        await window.__alekWriteJson("native-result.json", {
            ok: false,
            kind: "scene-opening",
            error: String(error.stack || error),
            checks,
            previews,
            diagnostic: window.__nativeOpeningAcceptance?.snapshot(),
        });
    }
    window.__alekWriteText("tail.txt", "drained before exit");
    window.__alekAPI("app.exit", {});
})();
