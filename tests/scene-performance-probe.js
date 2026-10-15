// Matched private-desktop Canvas2D work, not ordinary foreground FPS/GPU utilization.
(async function scenePerformance() {
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const percentile = (values, p) => [...values].sort((a, b) => a - b)[Math.floor((values.length - 1) * p)];
    const frame = () => new Promise((resolve) => requestAnimationFrame(resolve));
    let metricIndex = 0;
    const metrics = async () => {
        const name = "perf-" + metricIndex++;
        window.__nativeMetricsRequest = { name };
        for (let turn = 0; turn < 200; turn++) {
            if (window.__nativeMetricsResult?.name === name) return window.__nativeMetricsResult.sample;
            await new Promise((resolve) => setTimeout(resolve, 20));
        }
        throw new Error("Fixture metrics response timed out.");
    };
    const ownRenderer = (sample) => sample.processes.find((process) => process.kind === "Renderer");
    const rows = [],
        views = [];
    try {
        await window.__ALEK_BOOT_PROMISE__;
        const access = window.__nativeSceneAcceptance;
        await access.setup();
        await access.map();
        const Canvas = window.AlekrythaeWorldMap.SceneCanvas;
        for (let index = 0; index < 2; index++) {
            const canvas = document.createElement("canvas");
            canvas.style.cssText =
                "position:fixed;left:0;top:0;opacity:0;pointer-events:none;width:1280px;height:720px";
            document.body.append(canvas);
            views.push(
                new Canvas(canvas, { resolveJoAMediaUrl: () => "https://alek-assets.local/Assets/Bluemoon.png" }),
            );
        }
        const current = Canvas.prototype.draw,
            baseline = window.__sceneStaticBaseline;
        assert(baseline, "Frozen production baseline missing.");
        const methods = [baseline, current];
        const content = {
            strokes: Array.from({ length: 512 }, (_, index) => ({
                tool: ["foliage", "rock", "snow", "water"][index % 4],
                color: "#559d7b",
                size: 8,
                opacity: 0.8,
                seed: index + 1,
                points: Array.from({ length: 16 }, (_, point) => ({
                    x: (index % 32) * 18 + point * 2 - 300,
                    y: Math.floor(index / 32) * 24 - 180,
                })),
            })),
            images: Array.from({ length: 48 }, (_, index) => ({
                id: String(index),
                path: "shared-" + (index % 3) + ".png",
                width: 48,
                height: 48,
                position: { x: (index % 8) * 85 - 300, y: Math.floor(index / 8) * 65 - 180 },
            })),
        };
        for (const view of views) await view.prepare(content);
        const pixels = (view) => view.context.getImageData(0, 0, view.canvas.width, view.canvas.height).data;
        const samePixels = (label) => {
            const a = pixels(views[0]),
                b = pixels(views[1]);
            assert(
                a.length === b.length && a.every((byte, index) => byte === b[index]),
                "Visible pixels changed: " + label,
            );
        };
        for (const mode of ["static", "pan", "preview", "after-preview", "resize"]) {
            const preview =
                mode === "preview"
                    ? {
                          tool: "river",
                          color: "#559d7b",
                          size: 8,
                          opacity: 0.8,
                          seed: 9,
                          points: [
                              { x: 0, y: 0 },
                              { x: 10, y: 12 },
                          ],
                      }
                    : null;
            for (let index = 0; index < 2; index++) {
                const view = views[index];
                view.camera.x = mode === "pan" ? 120 : 0;
                view.canvas.style.width = mode === "resize" ? "1024px" : "1280px";
                methods[index].call(view, content, preview);
            }
            samePixels(mode);
        }
        for (const view of views) {
            view.canvas.style.width = "1280px";
            view.camera.x = 0;
        }
        for (let round = 0; round < 4; round++) {
            const index = [0, 1, 1, 0][round],
                view = views[index],
                draw = methods[index];
            for (let warm = 0; warm < 4; warm++) draw.call(view, content);
            const before = await metrics(),
                draws = [],
                gaps = [];
            let previous = performance.now();
            for (let turn = 0; turn < 120; turn++) {
                await frame();
                const now = performance.now();
                gaps.push(now - previous);
                const start = performance.now();
                draw.call(view, content);
                draws.push(performance.now() - start);
                previous = now;
            }
            const after = await metrics();
            const a = ownRenderer(before),
                b = ownRenderer(after);
            assert(a && b && a.id === b.id, "Renderer process changed during matched phase.");
            rows.push({
                round,
                method: index ? "current" : "baseline",
                calls: 120,
                commandMedianMs: percentile(draws, 0.5),
                commandP95Ms: percentile(draws, 0.95),
                privateDesktopRafMedianMs: percentile(gaps, 0.5),
                privateDesktopRafP95Ms: percentile(gaps, 0.95),
                rendererCpuMs: b.cpuMs - a.cpuMs,
                rendererPrivateBeforeBytes: a.privateBytes,
                rendererPrivateAfterBytes: b.privateBytes,
                rendererWorkingSetBeforeBytes: a.workingSetBytes,
                rendererWorkingSetAfterBytes: b.workingSetBytes,
                elapsedMs: after.elapsedMs - before.elapsedMs,
                before,
                after,
            });
        }
        const transitions = [];
        for (let turn = 0; turn < 20; turn++) {
            for (let index = 0; index < 2; index++) {
                views[index].camera.x = turn * 7;
                methods[index].call(views[index], content);
            }
            samePixels("pan-" + turn);
            transitions.push(turn);
        }
        for (const view of views) {
            view.dispose();
            assert(view.images.size === 0, "Disposed image cache retained paths.");
        }
        await window.__alekWriteJson("native-result.json", {
            ok: true,
            kind: "scene-performance",
            rows,
            viewport: [1280, 720],
            devicePixelRatio,
            strokes: 512,
            pointsPerStroke: 16,
            images: 48,
            decodedPaths: 3,
            identicalVisiblePixels: true,
            cameraChangesVerified: transitions.length,
            foregroundPerformance: false,
            gpuUtilizationMeasured: false,
            scope: "Alternating equal-work Canvas2D phases in one private fixture; renderer CPU/process memory and callback intervals, not whole-app FPS",
        });
    } catch (error) {
        await window.__alekWriteJson("native-result.json", { ok: false, error: String(error.stack || error), rows });
    } finally {
        for (const view of views) {
            view.dispose();
            view.canvas.remove();
        }
    }
    window.__alekWriteText("tail.txt", "drained before exit");
    window.__alekAPI("app.exit", {});
})();
