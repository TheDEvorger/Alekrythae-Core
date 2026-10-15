// Injected into the isolated legacy fixture only; never included in the application package.
window.__nativeAdventureTest = {
    load: loadAdventureRuntime,
    seed: record => seedAdventureFolder(record, {migrateLegacy:false}),
    register: record => adventureRegistry.adventures.push(record),
    session: () => joaSession.capture(),
    rename: (id, name, owner) => joaAdventureRecords.rename(id, name, owner),
    remove: (id, owner) => joaAdventureRecords.remove(id, owner),
    add: (record, time, owner) => joaAdventureRecords.add(record, time, owner),
    save: () => saveToDB(null),
    pendingModals: () => {
        const commit = async () => {
            storyData.__nativeCallbackCount = (storyData.__nativeCallbackCount || 0) + 1;
            return await saveToDB(null);
        };
        return {
            editor: showV2Modal({title:"Native session editor",body:"Isolated test",onSave:commit}),
            seal: showSealWordModal({title:"Native session seal",keyword:"test",onSuccess:commit})
        };
    },
    snapshot: () => ({
        id:activeAdventure?.id,registryId:adventureRegistry.activeId,baselineId:__dataBaselineAdventureId,
        path:DATA_PATHS.journey,
        roots:[storyData,characterData,locationData,groupData,mountData,forgottenData,specialData]
    })
};
