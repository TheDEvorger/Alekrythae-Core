// Finite real-surface exercise against the isolated Core SQLite adventure.
window.__sceneSessionAcceptance = async () => {
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const access = window.__nativeSceneAcceptance,
        surface = access.surface();
    const folder = surface.owner.folder,
        origin = surface.sceneId;
    const hero = { entityKey: "character:acceptance-hero" };
    assert(hero, "Fixture character missing.");
    const vehicleKey = "mount:acceptance-vehicle",
        destination = "acceptance-room";
    const commands = [
        ["placeEntity", { entityKey: hero.entityKey, kind: "character", name: "Acceptance hero", sceneId: origin }],
        ["createScene", { id: destination, name: "Acceptance room", parentId: origin }],
        ["placeEntity", { entityKey: vehicleKey, kind: "vehicle", name: "Acceptance vehicle", sceneId: origin }],
        ["addDoor", { id: "acceptance-door-a", sceneId: origin, position: { x: 10, y: 0 } }],
        ["addDoor", { id: "acceptance-door-b", sceneId: destination, position: { x: 30, y: 40 } }],
        ["linkDoors", { sourceId: "acceptance-door-a", targetId: "acceptance-door-b" }],
    ];
    assert((await surface.change(commands)).ok, "Fixture doors/vehicle commit failed.");
    const image = document.createElement("canvas");
    image.width = image.height = 16;
    const context = image.getContext("2d");
    context.fillStyle = "#267842";
    context.fillRect(0, 0, 16, 16);
    const path = "Games/" + folder + "/Media/acceptance-image.png";
    assert(
        (await window.__alekAPI("fs.writeBinary", { path, base64: image.toDataURL().split(",")[1] })).ok,
        "Fixture image write failed.",
    );
    const imported = await surface.bridge.importJoASceneImage(destination, path);
    assert(imported.ok, "Fixture scene image import failed.");
    await surface.loadArt(destination);
    assert(
        (
            await surface.change(
                [
                    [
                        "addSceneImage",
                        {
                            sceneId: destination,
                            id: "acceptance-image",
                            path: imported.path,
                            position: { x: 0, y: 0 },
                            width: 16,
                            height: 16,
                        },
                    ],
                ],
                { render: false },
            )
        ).ok,
        "Scene image commit failed.",
    );
    assert(
        (await surface.change([["boardVehicle", { characterKey: hero.entityKey, vehicleKey }]])).ok,
        "Passenger boarding failed.",
    );
    surface.selectedKey = vehicleKey;
    surface.render();
    const editing = await window.__sceneEditTransitionAcceptance(surface, path);
    for (let turn = 0; turn < 6; turn++) {
        const door = surface.state.doors.find((item) => item.sceneId === surface.sceneId && item.targetDoorId);
        const target = surface.state.doors.find((item) => item.id === door.targetDoorId);
        await surface.clickDoor(door);
        const vehicle = surface.state.placements.find((item) => item.entityKey === vehicleKey);
        const passenger = surface.state.placements.find((item) => item.entityKey === hero.entityKey);
        assert(
            surface.sceneId === target.sceneId && vehicle.sceneId === target.sceneId,
            "Door travel did not reach target.",
        );
        assert(
            passenger.vehicleId === vehicleKey && passenger.sceneId === vehicle.sceneId,
            "Passenger was left behind.",
        );
        assert(
            vehicle.position.x === target.position.x + 1 && vehicle.position.y === target.position.y,
            "Vehicle arrival is not beside the target door.",
        );
        assert(
            surface.artCache.size === 1 && surface.view.images.size <= 1,
            "Inactive scene art/decoded images accumulated.",
        );
    }
    const reopened = await surface.bridge.beginJoASceneEdit();
    assert(reopened.ok, "Saved graph could not reopen.");
    try {
        const passenger = reopened.state.placements.find((item) => item.entityKey === hero.entityKey);
        assert(
            passenger.vehicleId === vehicleKey && passenger.sceneId === surface.sceneId,
            "Persisted passenger location differs.",
        );
        assert(
            reopened.state.doors.filter((item) => item.id.startsWith("acceptance-door")).length === 2,
            "Door pair was lost.",
        );
        assert(
            reopened.state.events.filter((event) => event.text.includes("kap\u0131s\u0131ndan ge\u00e7ti")).length >= 6,
            "Successful travel events were lost.",
        );
    } finally {
        surface.bridge.cancelJoASceneDraft(reopened.draft);
    }
    const art = await surface.bridge.readJoASceneArt(destination);
    assert(
        art.ok && art.exists && art.data.content.images.some((item) => item.path === imported.path),
        "Per-scene image reference did not persist.",
    );
    surface.dispose();
    assert(surface.artCache.size === 0 && surface.view.images.size === 0, "Surface disposal retained images.");
    return {
        trips: 6,
        editing,
        vehiclePassengers: true,
        graphReopen: true,
        imageSqliteRead: true,
        activeArtCaches: 1,
        retainedImageLimit: 1,
        disposed: true,
        wholeProcessRestart: false,
    };
};
