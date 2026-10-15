// Actual production F1 geometry in synthetic, isolated WebView2 fixtures.
(async function f1Acceptance() {
    let frame, diagnostic;
    const results = [];
    const assert = (condition, message) => {
        if (!condition) throw new Error(message);
    };
    const settle = (win) =>
        new Promise((resolve) => win.requestAnimationFrame(() => win.requestAnimationFrame(resolve)));
    const waitFor = async (predicate) => {
        for (let turn = 0; turn < 100; turn++) {
            if (predicate()) return;
            await new Promise((resolve) => setTimeout(resolve, 30));
        }
        throw new Error("F1 did not become ready.");
    };
    try {
        await window.__ALEK_BOOT_PROMISE__;
        const access = window.__nativeSceneAcceptance;
        await access.setup();
        frame = document.createElement("iframe");
        frame.style.cssText = "position:fixed;left:0;top:0;opacity:0;pointer-events:none;border:0";
        document.body.append(frame);
        const styles = Array.from(document.styleSheets, (sheet) =>
            Array.from(sheet.cssRules, (rule) => rule.cssText).join("\n"),
        ).join("\n");
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
        for (const populated of [false, true])
            for (const linking of [false, true]) {
                const root = await access.f1(populated, linking);
                await waitFor(() => root?.querySelector(".joa-scene-f1-main"));
                const snapshot = root.cloneNode(true);
                for (const [width, height] of viewports) {
                    frame.style.width = width + "px";
                    frame.style.height = height + "px";
                    frame.srcdoc =
                        '<!doctype html><html><meta charset="utf-8"><style>' +
                        styles +
                        "</style><style>html,body{margin:0;width:100%;height:100%;overflow:hidden}</style><body></body></html>";
                    await new Promise((resolve) => frame.addEventListener("load", resolve, { once: true }));
                    const doc = frame.contentDocument;
                    doc.body.append(snapshot.cloneNode(true));
                    await settle(frame.contentWindow);
                    const label = `${width}x${height}/populated=${populated}/linking=${linking}`;
                    const host = doc.querySelector(".joa-scene-f1");
                    diagnostic = Array.from(
                        doc.querySelectorAll(
                            ".joa-scene-f1, .joa-scene-f1-main, .joa-scene-f1-tree, .joa-scene-f1-details",
                        ),
                        (node) => ({
                            class: node.className,
                            rect: node.getBoundingClientRect().toJSON(),
                            clientWidth: node.clientWidth,
                            scrollWidth: node.scrollWidth,
                            clientHeight: node.clientHeight,
                            scrollHeight: node.scrollHeight,
                        }),
                    );
                    const bounds = host.getBoundingClientRect();
                    assert(
                        bounds.width > 0 &&
                            bounds.left >= -1 &&
                            bounds.right <= width + 1 &&
                            bounds.bottom <= height + 1,
                        label + ": F1 outside viewport " + JSON.stringify(bounds),
                    );
                    for (const panel of doc.querySelectorAll(".joa-scene-f1-tree, .joa-scene-f1-details")) {
                        const box = panel.getBoundingClientRect();
                        assert(
                            box.height >= 40 && box.right <= width + 1 && box.bottom <= height + 1,
                            label + ": unusable panel " + panel.className + JSON.stringify(box),
                        );
                        assert(
                            panel.scrollWidth <= panel.clientWidth + 1,
                            label + ": horizontal overflow " + panel.className,
                        );
                    }
                    const tree = doc.querySelector(".joa-scene-f1-tree");
                    const treeBounds = tree.getBoundingClientRect();
                    for (const button of tree.querySelectorAll("button")) {
                        const box = button.getBoundingClientRect();
                        assert(
                            box.left >= treeBounds.left - 1 && box.right <= treeBounds.right + 1,
                            label + ": scene name/depth outside tree",
                        );
                    }
                    const details = doc.querySelector(".joa-scene-f1-details");
                    const editor = details.querySelector(".joa-scene-f1-editor");
                    assert(editor && !editor.open, label + ": hierarchy form must start collapsed");
                    editor.open = true;
                    await settle(frame.contentWindow);
                    const detailsBounds = details.getBoundingClientRect();
                    for (const control of details.querySelectorAll("button,input,select")) {
                        const box = control.getBoundingClientRect();
                        assert(
                            box.left >= detailsBounds.left - 1 && box.right <= detailsBounds.right + 1,
                            label +
                                ": detail control outside panel " +
                                (control.textContent || control.getAttribute("aria-label")),
                        );
                    }
                    const disjoint = (nodes) => {
                        let bottom = -Infinity;
                        for (const node of nodes) {
                            const box = node.getBoundingClientRect();
                            assert(
                                box.height >= 20 && box.top >= bottom - 1,
                                label + ": collapsed/overlapping rows " + node.className,
                            );
                            bottom = box.bottom;
                        }
                    };
                    disjoint(tree.children);
                    disjoint(details.children);
                    const last = [...details.querySelectorAll("button")].at(-1);
                    last.scrollIntoView({ block: "nearest" });
                    const lastBounds = last.getBoundingClientRect();
                    assert(
                        lastBounds.top >= detailsBounds.top - 1 && lastBounds.bottom <= detailsBounds.bottom + 1,
                        label + ": final hierarchy action cannot be reached by scrolling",
                    );
                    assert(
                        tree.querySelectorAll("button").length === (populated ? 35 : 3),
                        label + ": scene rows missing",
                    );
                    assert(
                        details.querySelectorAll(".joa-scene-f1-occupant").length === (populated ? 80 : 0),
                        label + ": occupants missing",
                    );
                    if (linking) assert(details.textContent.includes("bağla"), label + ": door linking action missing");
                    results.push({
                        width,
                        height,
                        populated,
                        linking,
                        treeHeight: treeBounds.height,
                        detailsHeight: detailsBounds.height,
                    });
                }
            }
        frame.remove();
        frame = null;
        await window.__alekWriteJson("native-result.json", {
            ok: true,
            kind: "scene-f1-geometry",
            cases: results.length,
            screenshotAcceptance: false,
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
