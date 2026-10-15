// Exercise normal app buttons on fixture data; no trusted OS mouse/keyboard input.
window.__sceneCreationAcceptance = async () => {
    const access = window.__nativeSceneAcceptance,
        bridge = access.surface().bridge;
    const initial = access.surface().sceneId;
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const wait = async (get, label) => {
        for (let attempt = 0; attempt < 100; attempt++) {
            const result = await get();
            if (result) return result;
            await new Promise((resolve) => setTimeout(resolve, 50));
        }
        throw new Error("Creation check timed out: " + label);
    };
    const state = async () => {
        const edit = await bridge.beginJoASceneEdit();
        assert(edit.ok, "Could not read creation graph.");
        bridge.cancelJoASceneDraft(edit.draft);
        return edit.state;
    };
    const button = (label) => {
        const item = [...document.querySelectorAll(".joa-scene-f1 button")].find((node) => node.textContent === label);
        assert(item, "F1 button missing: " + label);
        return item;
    };
    await access.f1(false, false);
    await wait(() => document.querySelector('.joa-scene-f1 input[aria-label="Yeni mek\u00e2n ad\u0131"]'), "F1 ready");
    const create = async (name, label) => {
        const input = document.querySelector('.joa-scene-f1 input[aria-label="Yeni mek\u00e2n ad\u0131"]');
        input.value = name;
        button(label).click();
        const scene = await wait(async () => (await state()).scenes.find((item) => item.name === name), name);
        await wait(async () => (await bridge.readJoASceneArt(scene.id)).exists, "new scene SQLite");
        return scene;
    };
    const root = await create("Creation root", "+ K\u00f6k");
    const child = await create("Creation child", "\u2193 Alt");
    assert(root.parentId === null && child.parentId === root.id, "F1 creation hierarchy incorrect.");
    const map = async (sceneId) => {
        window.__alekCloseJoAWorld?.({ save: false });
        window.__alekSceneOpenId = sceneId;
        await access.map();
        return wait(() => {
            const surface = access.surface();
            return (
                surface?.sceneId === sceneId && surface.overlay.classList.contains("scene-surface-active") && surface
            );
        }, "map " + sceneId);
    };
    let surface = await map(child.id);
    const previous = new Set(surface.state.doors.map((door) => door.id));
    await surface.placeDoor({ x: 8, y: 12 });
    const source = (await state()).doors.find((door) => !previous.has(door.id));
    assert(source?.sceneId === child.id && !source.targetDoorId, "New door did not persist before targeting.");
    await wait(() => document.querySelector(".joa-scene-f1-main"), "door target F1");
    const target = [...document.querySelectorAll(".joa-scene-f1-tree button")].find((node) =>
        node.textContent.includes(root.name),
    );
    assert(target, "Created root missing from door target tree.");
    target.click();
    button("Bu mek\u00e2na ba\u011fla").click();
    surface = await wait(() => {
        const item = access.surface();
        return item?.sceneId === root.id && item.overlay.classList.contains("scene-surface-active") && item;
    }, "target canvas");
    await surface.placeDoor({ x: 25, y: 30 });
    const saved = await state(),
        linked = saved.doors.find((door) => door.id === source.id);
    const arrival = saved.doors.find((door) => door.id === linked.targetDoorId);
    assert(
        arrival?.sceneId === root.id && arrival.targetDoorId === source.id,
        "New reciprocal door endpoints missing.",
    );
    assert(!window.__alekDoorDraft, "Completed door target draft was retained.");
    await map(initial);
    return {
        rootAndChildButtons: true,
        sceneSqliteReady: true,
        doorPlacementAndF1Target: true,
        reciprocalNewDoors: true,
        nativeRandomUuidAvailable: typeof crypto.randomUUID === "function",
        foregroundInputTested: false,
    };
};
