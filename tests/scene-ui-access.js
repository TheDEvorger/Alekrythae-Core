// Injected into an isolated test fixture, never shipped in the app.
window.__nativeSceneAcceptance = {
    async enter() {
        // This fixture tests the mounted screens; opening/portal animation is outside its scope.
        for (let turn = 0; turn < 200; turn++) {
            const splash = document.querySelector(".alek-cosmic-opening");
            if (splash) {
                splash.remove();
                window.__ALEK_COSMIC_OPENING_LOCKED__ = false;
                mainPanel.style.opacity = "1";
                mainPanel.style.pointerEvents = "auto";
                settleAdventureInterfaceVisibility();
                const moon = v9Masthead?.querySelector(".v9-core-orb");
                if (moon) moon.style.opacity = "1";
                return;
            }
            await new Promise((resolve) => setTimeout(resolve, 50));
        }
        throw new Error("Fixture opening screen was not ready to bypass.");
    },
    async setup({ resume = false } = {}) {
        await this.enter();
        const record = {
            id: "scene-ui-acceptance",
            folder: "Scene UI Acceptance",
            name: "Scene UI Acceptance",
            mode: "alekrytha",
            coverPolicy: "none",
            cover: "",
        };
        if (!adventureRegistry.adventures.some((item) => item.id === record.id))
            adventureRegistry.adventures.push(record);
        if (!resume) await seedAdventureFolder(record, { migrateLegacy: false });
        const loaded = await loadAdventureRuntime(record, { persistCurrent: false, notice: false });
        if (!loaded)
            throw new Error(
                "Fixture adventure load rejected: " +
                    JSON.stringify({
                        owner: window.__ALEK_ORCHESTRATOR__
                            .getModule("joa.world-experience")
                            .controller.bridge.getAdventureSession(),
                        transfer: window.__ALEK_DATA_TRANSFER_LOCK__,
                        opening: window.__ALEK_COSMIC_OPENING_LOCKED__,
                    }),
            );
        overlay.style.display = "flex";
        mainPanel.style.display = "flex";
        v2Shell.style.display = "flex";
    },
    async render(view, populated, messages) {
        window.__alekCloseJoAWorld?.({ save: false });
        const d = ensureSelectedDate();
        storyData.messages =
            messages ||
            (populated
                ? Array.from({ length: 12 }, (_, index) => ({
                      id: "acceptance-message-" + index,
                      year: d.year,
                      month: d.month,
                      day: d.day,
                      order: index + 1,
                      speakerType: "meggy",
                      speakerName: "Acceptance speaker",
                      speakerTitle: "",
                      text: index === 0 ? "LongToken".repeat(250) : "Acceptance message " + index,
                      createdAt: new Date().toISOString(),
                      durationSeconds: 0,
                  }))
                : []);
        await window.__alekNavigatePrimarySurface(view === "ai" ? "ai" : "tavern");
        return overlay;
    },
    async map() {
        window.__ALEK_COSMIC_OPENING_LOCKED__ = false;
        await window.__alekNavigatePrimarySurface("map");
        return document.querySelector(".joa-world-shell");
    },
    async copyEntries(view) {
        const log = window.AlekrythaeEventLogView;
        if (view !== "ai") return log.collect(storyData.messages);
        const sceneResult = await joaScenes.begin();
        if (sceneResult.draft) joaScenes.cancel(sceneResult.draft);
        if (!sceneResult.ok) throw new Error(sceneResult.error);
        const events = [...(sceneResult.state.events || [])];
        for (const scene of sceneResult.state.scenes) {
            const art = await window.__alekAPI("db.scene.read", { folder: activeAdventure.folder, sceneId: scene.id });
            if (art.ok && art.exists) events.push(...(art.data.content.events || []));
        }
        const gameplay = await joaDocumentStore.read(activeAdventure.folder, "gameEvents");
        if (!gameplay.ok) throw new Error(gameplay.error);
        return log.collect(storyData.messages, events, [
            ...(storyData.game?.joaContainmentEvents || []),
            ...(gameplay.exists ? gameplay.data.events : []),
        ]);
    },
    async f1(populated, linking, { depth = 32, occupants = 80 } = {}) {
        window.__alekCloseJoAWorld?.({ save: false });
        const bridge = window.__ALEK_ORCHESTRATOR__.getModule("joa.world-experience").controller.bridge;
        if (populated && !this.f1Populated) {
            const draft = await bridge.beginJoASceneEdit();
            if (!draft.ok) throw new Error(draft.error);
            try {
                let parent = draft.state.initialSceneId;
                for (let index = 0; index < depth; index++) {
                    const id = "f1-deep-" + index;
                    const result = bridge.editJoASceneDraft(draft.draft, "createScene", {
                        id,
                        parentId: parent,
                        name: "Mekân " + index + " " + "Uzunİsim".repeat(40),
                    });
                    if (!result.ok) throw new Error(result.error);
                    parent = id;
                }
                for (let index = 0; index < occupants; index++) {
                    const vehicle = index % 4 === 0;
                    const result = bridge.editJoASceneDraft(draft.draft, "placeEntity", {
                        entityKey: (vehicle ? "mount:" : "character:") + "f1-" + index,
                        kind: vehicle ? "vehicle" : "character",
                        sceneId: draft.state.initialSceneId,
                        name: "Varlık " + index + " " + "Uzunİsim".repeat(40),
                    });
                    if (!result.ok) throw new Error(result.error);
                }
                const saved = await bridge.commitJoASceneDraft(draft.draft);
                if (!saved.ok) throw new Error(saved.error);
                this.f1Populated = true;
            } finally {
                bridge.cancelJoASceneDraft(draft.draft);
            }
        }
        if (linking)
            window.__alekDoorDraft = {
                owner: bridge.getAdventureSession(),
                sourceId: "layout-only-door",
                sourceSceneId: "unknown-region",
            };
        else delete window.__alekDoorDraft;
        await window.__alekNavigatePrimarySurface("inventory");
        return document.querySelector(".joa-inventory-page-shell");
    },
    layoutSyncSources: () => [syncV53ViewportLayout.toString(), syncV54InlineLayout.toString()],
    surface: () => window.__ALEK_ORCHESTRATOR__.getModule("joa.world-experience").controller.sceneSurface,
};
