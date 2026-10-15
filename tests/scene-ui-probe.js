// Browser geometry checks against actual rendered production markup and styles.
// This self-test does not use screenshots or claim visual/artistic acceptance.
(async function sceneUiAcceptance() {
    const results = [];
    let frame, timeout, diagnostic;
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const settle = (win) =>
        new Promise((resolve) => win.requestAnimationFrame(() => win.requestAnimationFrame(resolve)));
    const waitFor = async (predicate) => {
        for (let turn = 0; turn < 100; turn++) {
            if (predicate()) return;
            await new Promise((resolve) => setTimeout(resolve, 30));
        }
        throw new Error("Acceptance view was not ready.");
    };
    try {
        await Promise.race([
            window.__ALEK_BOOT_PROMISE__,
            new Promise((_, reject) => {
                timeout = setTimeout(() => reject(new Error("Boot timeout")), 60000);
            }),
        ]);
        clearTimeout(timeout);
        const access = window.__nativeSceneAcceptance;
        assert(access, "Acceptance test access missing.");
        await access.setup();
        const copyControls = await window.__sceneCopyAcceptance();
        frame = document.createElement("iframe");
        frame.style.cssText = "position:fixed;left:0;top:0;opacity:0;pointer-events:none;border:0;z-index:2147483646";
        document.body.append(frame);
        const styles = () =>
            Array.from(document.styleSheets, (sheet) =>
                Array.from(sheet.cssRules, (rule) => rule.cssText).join("\n"),
            ).join("\n");
        const install = async (root, width, height) => {
            frame.style.width = width + "px";
            frame.style.height = height + "px";
            frame.srcdoc =
                '<!doctype html><html><meta charset="utf-8"><style>' +
                styles() +
                "</style><style>html,body{margin:0;width:100%;height:100%;overflow:hidden}</style><body></body></html>";
            await new Promise((resolve) => frame.addEventListener("load", resolve, { once: true }));
            frame.contentDocument.body.append(root.cloneNode(true));
            await settle(frame.contentWindow);
            for (const source of access.layoutSyncSources()) frame.contentWindow.eval("(" + source + ")()");
            await settle(frame.contentWindow);
            for (const source of access.layoutSyncSources()) frame.contentWindow.eval("(" + source + ")()");
        };
        const viewports = [
            [1920, 1080],
            [1366, 768],
            [1024, 768],
            [820, 614],
            [683, 512],
            [512, 384],
            [320, 568],
            [568, 320],
        ];
        for (const view of ["encounter", "ai"])
            for (const populated of [false, true]) {
                const root = await access.render(view, populated);
                await waitFor(() => view !== "ai" || root.querySelector(".joa-event-log .joa-event-copy-controls"));
                await settle(window);
                const snapshot = root.cloneNode(true);
                for (const [width, height] of viewports) {
                    await install(snapshot, width, height);
                    const doc = frame.contentDocument;
                    const surface = doc.querySelector(view === "ai" ? ".joa-event-log" : ".tavern-stage");
                    const box = surface.getBoundingClientRect();
                    const label = width + "x" + height + "/" + view + "/" + populated;
                    diagnostic = [];
                    for (let node = surface; node; node = node.parentElement) {
                        const css = frame.contentWindow.getComputedStyle(node);
                        diagnostic.push({
                            id: node.id,
                            class: node.className,
                            inline: node.getAttribute("style"),
                            width: css.width,
                            height: css.height,
                            minWidth: css.minWidth,
                            zoom: css.zoom,
                            rect: node.getBoundingClientRect().toJSON(),
                        });
                    }
                    assert(
                        box.width > 0 && box.left >= -1 && box.right <= width + 1,
                        label + ": surface outside viewport " + JSON.stringify(box),
                    );
                    assert(surface.scrollWidth <= surface.clientWidth + 1, label + ": horizontal surface overflow.");
                    const controls = surface.querySelector(".joa-event-copy-controls");
                    assert(
                        controls && controls.querySelectorAll("button").length === 3,
                        label + ": copy buttons missing.",
                    );
                    for (const button of controls.querySelectorAll("button")) {
                        const rect = button.getBoundingClientRect();
                        assert(
                            rect.left >= box.left - 1 && rect.right <= Math.min(width, box.right) + 1,
                            label + ": copy button outside surface.",
                        );
                    }
                    if (view === "encounter") {
                        const columns = surface.querySelector(".tavern-columns");
                        assert(columns.scrollWidth <= columns.clientWidth + 1, label + ": Tavern columns overflow.");
                        const log = surface.querySelector(".tavern-log").getBoundingClientRect();
                        assert(log.height >= 30, label + ": message history collapsed " + JSON.stringify(log));
                        const composer = surface.querySelector(".tavern-composer-shell").getBoundingClientRect();
                        assert(composer.width > 0 && composer.right <= width + 1, label + ": composer overflow.");
                        assert(composer.bottom <= height + 1, label + ": composer below viewport.");
                        const parts = [...columns.children].map((node) => node.getBoundingClientRect());
                        for (const part of parts)
                            assert(
                                part.top >= box.top && part.bottom <= box.bottom + 1,
                                label + ": Tavern panel outside stage " + JSON.stringify(part),
                            );
                        for (let first = 0; first < parts.length; first++)
                            for (let second = first + 1; second < parts.length; second++) {
                                const a = parts[first],
                                    b = parts[second];
                                const overlapX = Math.min(a.right, b.right) - Math.max(a.left, b.left);
                                const overlapY = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
                                assert(overlapX <= 1 || overlapY <= 1, label + ": Tavern panels overlap.");
                            }
                        assert(log.bottom <= composer.top + 1, label + ": composer covers message history.");
                    }
                    results.push({ view, populated, width, height, surfaceWidth: box.width });
                }
            }
        const map = await access.map();
        await waitFor(() => map?.classList.contains("scene-surface-active"));
        for (const [width, height] of viewports) {
            await install(map, width, height);
            const doc = frame.contentDocument;
            const scale = doc.querySelector(".joa-scale-bar");
            assert(scale && frame.contentWindow.getComputedStyle(scale).display !== "none", "Scene scale hidden.");
            for (const selector of [
                ".joa-layer-switch",
                ".joa-map-badge",
                ".joa-map-legend",
                ".joa-discovery-status",
            ]) {
                const node = doc.querySelector(selector);
                assert(
                    !node || frame.contentWindow.getComputedStyle(node).display === "none",
                    "Retired chrome visible.",
                );
            }
            const box = scale.getBoundingClientRect();
            assert(
                box.left >= 0 && box.right <= width + 1 && box.top >= 0 && box.bottom <= height + 1,
                width + "x" + height + ": scale outside viewport.",
            );
            const palette = doc.querySelector(".joa-scene-tools");
            assert(palette.getBoundingClientRect().width <= 65, "Hover palette does not collapse.");
            palette.focus();
            await new Promise((resolve) => setTimeout(resolve, 200));
            const expanded = palette.getBoundingClientRect();
            assert(expanded.width > 65 && expanded.right <= width + 1, "Focused palette does not fit.");
            assert(
                frame.contentWindow.getComputedStyle(palette.querySelector("strong")).visibility === "visible",
                "Palette controls remain hidden on keyboard focus.",
            );
            results.push({ view: "scene-map", width, height, scaleVisible: true, paletteCollapsed: true });
        }
        const creation = await window.__sceneCreationAcceptance();
        const session = await window.__sceneSessionAcceptance();
        const renderer = await window.__sceneRendererBenchmark();
        frame.remove();
        frame = null;
        await window.__alekWriteJson("native-result.json", {
            ok: true,
            kind: "scene-ui-geometry",
            cases: results.length,
            screenshotAcceptance: false,
            copyControls,
            session,
            creation,
            renderer,
            results,
        });
    } catch (error) {
        frame?.remove();
        await window.__alekWriteJson("native-result.json", {
            ok: false,
            error: String(error.stack || error),
            diagnostic,
            results,
        });
    }
    window.__alekWriteText("tail.txt", "drained before exit");
    window.__alekAPI("app.exit", {});
})();
