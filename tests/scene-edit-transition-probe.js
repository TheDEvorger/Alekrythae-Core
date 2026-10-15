// Own embedded Canvas2D/SQLite fixture only; no OS input, clipboard or user saves.
window.__sceneEditTransitionAcceptance = async (surface, imagePath) => {
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const pixels = () => surface.view.context.getImageData(0, 0, surface.canvas.width, surface.canvas.height).data;
    const equal = (a, b) => a.length === b.length && a.every((value, i) => value === b[i]);
    const pointer = (point) => {
        const screen = surface.view.screen(point),
            rect = surface.canvas.getBoundingClientRect();
        return { button: 0, pointerId: 17, clientX: rect.left + screen.x, clientY: rect.top + screen.y };
    };
    const down = (point) => {
        // Direct method exercise has no active OS pointer; capture is outside this check's scope.
        const canvas = surface.canvas,
            capture = canvas.setPointerCapture;
        const own = Object.hasOwn(canvas, "setPointerCapture");
        canvas.setPointerCapture = () => {};
        try {
            surface.down(pointer(point));
        } finally {
            if (own) canvas.setPointerCapture = capture;
            else delete canvas.setPointerCapture;
        }
    };
    const origin = surface.sceneId,
        originalCamera = { ...surface.view.camera };
    const stable = () => {
        const before = pixels();
        surface.render();
        assert(equal(before, pixels()), "Unchanged frame lost pixels.");
        return before;
    };
    const draw = async (tool, start, end) => {
        surface.tool = tool;
        down(start);
        surface.move(pointer(end));
        await surface.up();
    };
    let before = stable();
    await draw("brush", { x: -65, y: -45 }, { x: -25, y: -30 });
    assert(!equal(before, pixels()), "Saved brush did not invalidate unchanged frame.");
    before = stable();
    await draw("foliage", { x: -70, y: 40 }, { x: -30, y: 55 });
    assert(!equal(before, pixels()), "Seeded foliage did not update pixels.");
    stable();
    surface.imagePurpose = "scene";
    const ids = new Set(surface.content().images.map((image) => image.id));
    await surface.acceptImage(imagePath);
    const added = surface.content().images.find((image) => !ids.has(image.id));
    assert(added && added.path.includes("/Mekanlar/"), "Normal image was not imported into scene storage.");
    surface.tool = "pan";
    const moveStart = { x: added.position.x + 10, y: added.position.y + 10 };
    before = stable();
    down(moveStart);
    surface.move(pointer({ x: moveStart.x + 45, y: moveStart.y + 35 }));
    const movedPreview = pixels();
    assert(!equal(before, movedPreview), "Image drag preview did not redraw.");
    await surface.up();
    assert(equal(movedPreview, pixels()), "Committed image drag differs from preview.");
    const moved = surface.content().images.find((image) => image.id === added.id);
    assert(
        moved.position.x === added.position.x + 45 && moved.position.y === added.position.y + 35,
        "Image position was not saved.",
    );
    const corner = { x: moved.position.x + moved.width - 1, y: moved.position.y + moved.height - 1 };
    before = stable();
    down(corner);
    surface.move(pointer({ x: corner.x + 20, y: corner.y + 15 }));
    const resizedPreview = pixels();
    assert(!equal(before, resizedPreview), "Image resize preview did not redraw.");
    await surface.up();
    assert(equal(resizedPreview, pixels()), "Committed resize differs from preview.");
    const content = structuredClone(surface.content()),
        savedPixels = stable();
    const bridge = surface.bridge;
    surface.bridge = { ...bridge, writeJoASceneArt: async () => ({ ok: false, error: "injected art save failure" }) };
    try {
        await draw("brush", { x: -30, y: -80 }, { x: 45, y: -75 });
        assert(
            JSON.stringify(content) === JSON.stringify(surface.content()),
            "Failed brush mutated cached art/events.",
        );
        assert(equal(savedPixels, pixels()), "Failed brush left a preview in the bitmap.");
        surface.tool = "pan";
        down({ x: moved.position.x + 10, y: moved.position.y + 10 });
        surface.move(pointer({ x: moved.position.x + 80, y: moved.position.y + 50 }));
        assert(!equal(savedPixels, pixels()), "Failed-drag preview did not move.");
        await surface.up();
        assert(equal(savedPixels, pixels()), "Failed image save did not restore original pixels.");
        assert(JSON.stringify(content) === JSON.stringify(surface.content()), "Failed image save mutated art/events.");
    } finally {
        surface.bridge = bridge;
    }
    const stored = await surface.bridge.readJoASceneArt(origin);
    assert(
        stored.ok && JSON.stringify(stored.data.content) === JSON.stringify(content),
        "Brush/image state differs from SQLite after failed edits.",
    );
    const prepare = surface.view.prepare;
    let ready, release;
    const entered = new Promise((resolve) => {
        ready = resolve;
    });
    const gate = new Promise((resolve) => {
        release = resolve;
    });
    surface.view.prepare = async function (art) {
        ready();
        await gate;
        return prepare.call(this, art);
    };
    const door = surface.state.doors.find((item) => item.sceneId === origin && item.targetDoorId);
    const trip = surface.clickDoor(door);
    try {
        await entered;
        const held = stable();
        assert(
            surface.sceneId === origin && surface.content().images.some((image) => image.id === added.id),
            "Source art closed before destination readiness.",
        );
        const travelEvents = () =>
            surface.state.events.filter((event) => event.text.includes("kap\u0131s\u0131ndan ge\u00e7ti")).length;
        const count = travelEvents();
        await surface.clickDoor(door);
        assert(surface.sceneId === origin && equal(held, pixels()), "Second door click disturbed the held source.");
        assert(travelEvents() === count, "Travel was logged before target readiness.");
        release();
        await trip;
        assert(
            surface.sceneId !== origin && travelEvents() === count + 1,
            "Serialized trip did not commit exactly once.",
        );
    } finally {
        release();
        surface.view.prepare = prepare;
        await trip;
    }
    const returnDoor = surface.state.doors.find((item) => item.sceneId === surface.sceneId && item.targetDoorId);
    await surface.clickDoor(returnDoor);
    assert(surface.sceneId === origin, "Return trip did not reopen edited scene.");
    assert(
        JSON.stringify(content) === JSON.stringify(surface.content()),
        "Edited art changed after leaving and returning.",
    );
    const arrivalCamera = { ...surface.view.camera };
    Object.assign(surface.view.camera, originalCamera);
    surface.render();
    assert(equal(savedPixels, pixels()), "Returned scene bitmap differs at the original camera.");
    Object.assign(surface.view.camera, arrivalCamera);
    surface.render();
    return {
        normalBrush: true,
        seededBrush: true,
        imageMoveResize: true,
        failedEditsRestorePixels: true,
        sqliteExact: true,
        returnPixelsExact: true,
        pendingTravelHoldsSource: true,
        duplicateTravelRejected: true,
        editReturnTrips: 2,
        foregroundInputTested: false,
    };
};
