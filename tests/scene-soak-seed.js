// Populated synthetic scene fixture; source adventures are never opened for mutation.
window.__sceneSoakSeed = async (surface) => {
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const origin = surface.sceneId;
    const scenes = [origin, ...Array.from({ length: 5 }, (_, i) => "soak-room-" + i)];
    const vehicleKey = "mount:soak-vehicle";
    const passengers = Array.from({ length: 10 }, (_, i) => "character:soak-passenger-" + i);
    const steps = scenes.slice(1).map((id) => ["createScene", { id, name: "Soak room " + id, parentId: origin }]);
    steps.push(["placeEntity", { entityKey: vehicleKey, kind: "vehicle", name: "Soak vehicle", sceneId: origin }]);
    for (const entityKey of passengers)
        steps.push(["placeEntity", { entityKey, kind: "character", name: entityKey, sceneId: origin }]);
    for (let index = 0; index < 72; index++) {
        const kind = index % 6 === 0 ? "vehicle" : "character";
        steps.push([
            "placeEntity",
            {
                entityKey: (kind === "vehicle" ? "mount:" : "character:") + "soak-extra-" + index,
                kind,
                name: "Synthetic occupant " + index,
                sceneId: scenes[index % scenes.length],
                position: { x: index * 7, y: 35 },
            },
        ]);
    }
    for (let index = 0; index < scenes.length; index++) {
        steps.push(["addDoor", { id: "soak-out-" + index, sceneId: scenes[index], position: { x: 50, y: 0 } }]);
        steps.push(["addDoor", { id: "soak-in-" + index, sceneId: scenes[index], position: { x: 0, y: 0 } }]);
    }
    for (let index = 0; index < scenes.length; index++)
        steps.push([
            "linkDoors",
            {
                sourceId: "soak-out-" + index,
                targetId: "soak-in-" + ((index + 1) % scenes.length),
            },
        ]);
    for (const characterKey of passengers) steps.push(["boardVehicle", { characterKey, vehicleKey }]);
    assert((await surface.change(steps)).ok, "Populated graph seed failed.");
    const sources = [];
    for (let index = 0; index < 3; index++) {
        const canvas = document.createElement("canvas");
        canvas.width = canvas.height = 128;
        const context = canvas.getContext("2d");
        context.fillStyle = ["#267842", "#4e88ab", "#d3ceba"][index];
        context.fillRect(0, 0, 128, 128);
        const path = "Games/" + surface.owner.folder + "/Media/soak-" + index + ".png";
        assert(
            (await window.__alekAPI("fs.writeBinary", { path, base64: canvas.toDataURL().split(",")[1] })).ok,
            "Synthetic PNG write failed.",
        );
        sources.push(path);
    }
    for (const sceneId of scenes) {
        const paths = [];
        for (const source of sources) {
            const imported = await surface.bridge.importJoASceneImage(sceneId, source);
            assert(imported.ok, "Scene image import failed.");
            paths.push(imported.path);
        }
        const previous = await window.AlekrythaeSceneArtStore.read(surface.bridge, surface.state, sceneId);
        assert(previous.ok && !previous.legacy, "SQLite scene art missing.");
        const content = {
            strokes: Array.from({ length: 64 }, (_, index) => ({
                tool: ["foliage", "river", "rock", "snow"][index % 4],
                color: "#559d7b",
                size: 8,
                opacity: 0.8,
                seed: index + 1,
                points: Array.from({ length: 16 }, (_, point) => ({
                    x: (index % 8) * 90 + point * 3 - 280,
                    y: Math.floor(index / 8) * 60 - 200,
                })),
            })),
            images: Array.from({ length: 48 }, (_, index) => ({
                id: "soak-image-" + index,
                path: paths[index % 3],
                width: 64,
                height: 64,
                position: { x: (index % 8) * 100 - 350, y: Math.floor(index / 8) * 90 - 240 },
            })),
        };
        assert(
            (
                await surface.bridge.writeJoASceneArt(
                    sceneId,
                    {
                        schemaVersion: 1,
                        sceneId,
                        revision: previous.revision + 1,
                        content,
                    },
                    previous.revision,
                )
            ).ok,
            "Populated art seed failed.",
        );
    }
    return { scenes, vehicleKey, passengers };
};
