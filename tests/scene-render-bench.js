// Real Canvas2D command timings. This is not whole-app FPS, CPU/GPU utilization or OS memory.
window.__sceneRendererBenchmark = async () => {
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const Canvas = window.AlekrythaeWorldMap.SceneCanvas;
    const canvas = document.createElement("canvas");
    canvas.style.cssText = "position:fixed;left:0;top:0;opacity:0;pointer-events:none;width:800px;height:600px";
    document.body.append(canvas);
    const view = new Canvas(canvas, {
        resolveJoAMediaUrl: () => "https://alek-assets.local/Assets/Bluemoon.png",
    });
    try {
        const current = Canvas.prototype.draw,
            source = current.toString();
        const start = source.indexOf("const bounds = this.visibleBounds;");
        const end = source.indexOf("if (loaded?.complete", start);
        assert(start >= 0 && end > start, "Image culling baseline could not be identified.");
        const previous = new Function(
            "return function" + source.slice(source.indexOf("("), start) + source.slice(end),
        )();
        const draw = (method, content) => {
            view.frames?.clear(); // Compare culling with full repaints on both sides.
            method.call(view, content);
        };
        const median = (values) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
        const rows = [];
        for (const count of [1000, 10000]) {
            const content = {
                strokes: [],
                images: Array.from({ length: count }, (_, index) => ({
                    id: String(index),
                    path: "benchmark.png",
                    width: 16,
                    height: 16,
                    position:
                        index % 100 === 0
                            ? { x: (index % 200) - 100, y: (index % 150) - 75 }
                            : { x: 100000 + index * 20, y: 100000 },
                })),
            };
            await view.prepare(content);
            draw(previous, content);
            const before = view.context.getImageData(0, 0, canvas.width, canvas.height).data;
            draw(current, content);
            const after = view.context.getImageData(0, 0, canvas.width, canvas.height).data;
            assert(
                before.length === after.length && before.every((byte, index) => byte === after[index]),
                "Image culling changed visible pixels.",
            );
            for (let turn = 0; turn < 4; turn++) {
                draw(previous, content);
                draw(current, content);
            }
            const timings = [[], []];
            for (let turn = 0; turn < 21; turn++) {
                for (const index of turn % 2 ? [1, 0] : [0, 1]) {
                    const startTime = performance.now();
                    draw(index ? current : previous, content);
                    timings[index].push(performance.now() - startTime);
                }
                await new Promise((resolve) => requestAnimationFrame(resolve));
            }
            rows.push({
                images: count,
                visibleImages: count / 100,
                previousMedianMs: median(timings[0]),
                currentMedianMs: median(timings[1]),
                retainedDecodedPaths: view.images.size,
                identicalVisiblePixels: true,
            });
        }
        return {
            scope: "Canvas2D command submission with 99% off-screen images; whole-app/GPU/memory unmeasured",
            viewport: [800, 600],
            devicePixelRatio: window.devicePixelRatio,
            rows,
        };
    } finally {
        view.dispose();
        canvas.remove();
    }
};
