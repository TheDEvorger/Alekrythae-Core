// Injected into a disposable package copy. Never removes or unlocks the opening.
window.__nativeOpeningAcceptance = {
    async seed() {
        const record = {
            id: "opening-acceptance",
            folder: "Opening Acceptance",
            name: "Opening Acceptance",
            mode: "alekrytha",
            coverPolicy: "alekrythae-default",
            cover: "Assets/alekrythae_logo.png",
        };
        await seedAdventureFolder(record, { migrateLegacy: false });
        adventureRegistry.adventures.push(record);
        return record.id;
    },
    snapshot() {
        return {
            locked: window.__ALEK_COSMIC_OPENING_LOCKED__,
            activeId: activeAdventure?.id,
            startConfigured: normalizeGameRuntime().time.startConfigured,
            sessionReady: joaSession.capture().ready,
            shellOpacity: getComputedStyle(v2Shell).opacity,
            shellPointerEvents: getComputedStyle(v2Shell).pointerEvents,
            mastheadMoonOpacity: getComputedStyle(mastheadMoon).opacity,
            mapMounted: !!document.querySelector(".joa-world-shell"),
        };
    },
    moon: () => mastheadMoon,
    map: () => window.__alekNavigatePrimarySurface("map"),
};
