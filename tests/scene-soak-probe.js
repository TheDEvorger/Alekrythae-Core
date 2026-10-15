// Finite populated session with four independent fixture processes, not a foreground benchmark.
(async function sceneSoak() {
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const waitFor = async (predicate) => {
        for (let turn = 0; turn < 400; turn++) {
            if (predicate()) return;
            await new Promise((resolve) => setTimeout(resolve, 30));
        }
        throw new Error("Scene fixture did not become ready.");
    };
    let surface, checkpoint, fixture;
    const draws = [];
    const snapshot = async () => {
        const reopened = await surface.bridge.beginJoASceneEdit();
        assert(reopened.ok, "Persisted graph reopen failed.");
        let graph;
        try {
            graph = JSON.stringify(reopened.state);
        } finally {
            surface.bridge.cancelJoASceneDraft(reopened.draft);
        }
        const art = [];
        for (const sceneId of fixture.scenes) {
            const read = await surface.bridge.readJoASceneArt(sceneId);
            assert(read.ok && read.exists, "Persisted scene SQLite missing: " + sceneId);
            art.push({ sceneId, data: JSON.stringify(read.data) });
        }
        return { graph, art };
    };
    try {
        await window.__ALEK_BOOT_PROMISE__;
        const restored = await window.__alekReadJson("soak-checkpoint.json");
        const savedCheckpoint = restored?.ok ? restored.data : null;
        checkpoint = Number.isInteger(savedCheckpoint?.phase) && savedCheckpoint.fixture ? savedCheckpoint : null;
        const access = window.__nativeSceneAcceptance;
        await access.setup({ resume: !!checkpoint });
        const bridge = window.__ALEK_ORCHESTRATOR__.getModule("joa.world-experience").controller.bridge;
        const reopen = async () => {
            if (fixture) {
                const begun = await bridge.beginJoASceneEdit();
                assert(begun.ok, "Focus graph read failed.");
                try {
                    const vehicle = begun.state.placements.find((item) => item.entityKey === fixture.vehicleKey);
                    window.__alekSceneFocus = {
                        owner: bridge.getAdventureSession(),
                        sceneId: vehicle.sceneId,
                        entityKey: fixture.vehicleKey,
                    };
                } finally {
                    bridge.cancelJoASceneDraft(begun.draft);
                }
            }
            await access.map();
            await waitFor(() => access.surface()?.state && access.surface().artCache.has(access.surface().sceneId));
            surface = access.surface();
            if (fixture) surface.selectedKey = fixture.vehicleKey;
            surface.render();
        };
        fixture = checkpoint?.fixture;
        await reopen();
        if (!checkpoint) {
            fixture = await window.__sceneSoakSeed(surface);
            const old = surface;
            await access.f1(false, false);
            assert(old.artCache.size === 0 && old.view.images.size === 0, "Closed map retained caches.");
            await reopen();
            checkpoint = { phase: 0, trips: 0, fixture };
        } else {
            assert(
                JSON.stringify(await snapshot()) === JSON.stringify(checkpoint.snapshot),
                "Whole-process restart changed graph/art.",
            );
        }
        const phase = checkpoint.phase;
        const rounds = phase < 3 ? 120 : 0;
        for (let turn = 0; turn < rounds; turn++) {
            const index = fixture.scenes.indexOf(surface.sceneId);
            const door = surface.state.doors.find((item) => item.id === "soak-out-" + index);
            const target = surface.state.doors.find((item) => item.id === door.targetDoorId);
            await surface.clickDoor(door);
            const vehicle = surface.state.placements.find((item) => item.entityKey === fixture.vehicleKey);
            assert(surface.sceneId === target.sceneId && vehicle.sceneId === target.sceneId, "Vehicle travel failed.");
            assert(
                vehicle.position.x === target.position.x + 1 && vehicle.position.y === target.position.y,
                "Arrival position differs.",
            );
            for (const key of fixture.passengers) {
                const passenger = surface.state.placements.find((item) => item.entityKey === key);
                assert(
                    passenger.vehicleId === fixture.vehicleKey && passenger.sceneId === vehicle.sceneId,
                    "Passenger left behind.",
                );
            }
            assert(surface.artCache.size === 1 && surface.view.images.size <= 3, "Inactive scene caches accumulated.");
            if (turn % 20 === 0) {
                assert(
                    (
                        await surface.change([
                            [
                                "addStroke",
                                {
                                    sceneId: surface.sceneId,
                                    tool: "foliage",
                                    color: "#559d7b",
                                    size: 8,
                                    opacity: 1,
                                    seed: phase * 1000 + turn,
                                    points: [{ x: turn, y: 0 }],
                                },
                            ],
                        ])
                    ).ok,
                    "Live brush save failed.",
                );
                const before = performance.now();
                surface.render();
                draws.push(performance.now() - before);
                const old = surface;
                await access.f1(false, false);
                assert(old.artCache.size === 0 && old.view.images.size === 0, "F1 navigation retained map caches.");
                await access.render("ai", false);
                await waitFor(() => document.querySelector(".joa-event-log-list"));
                await reopen();
            }
        }
        const trips = checkpoint.trips + rounds;
        const saved = await snapshot();
        const graph = JSON.parse(saved.graph);
        const travels = graph.events.filter((event) => event.text.includes("kap\u0131s\u0131ndan ge\u00e7ti"));
        assert(travels.length === trips, "Persisted successful travel events differ: " + travels.length + "/" + trips);
        for (const item of saved.art) {
            const art = JSON.parse(item.data);
            assert(art.content.images.length === 48 && art.content.strokes.length >= 64, "Populated art was lost.");
        }
        await window.__alekWriteJson("soak-checkpoint.json", { phase: phase + 1, trips, fixture, snapshot: saved });
        await window.__alekWriteJson("native-result.json", {
            ok: true,
            kind: "scene-populated-soak",
            phase,
            phaseTrips: rounds,
            totalTrips: trips,
            restoredPriorProcess: phase > 0,
            scenes: 6,
            placements: 83,
            passengers: 10,
            imagesPerScene: 48,
            seededStrokesPerScene: 64,
            activeArtCaches: surface.artCache.size,
            decodedImages: surface.view.images.size,
            commandSubmissionMs: draws,
            foregroundPerformance: false,
            gpuUtilizationMeasured: false,
        });
    } catch (error) {
        await window.__alekWriteJson("native-result.json", {
            ok: false,
            kind: "scene-populated-soak",
            error: String(error.stack || error),
            phase: checkpoint?.phase,
            sceneId: surface?.sceneId,
        });
    }
    window.__alekWriteText("tail.txt", "drained before exit");
    window.__alekAPI("app.exit", {});
})();
