// Real JOA runtime + Core SQLite; every document belongs to the isolated native fixture.
(async function probeAdventureLoading() {
    let timeout;
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    try {
        await Promise.race([window.__ALEK_BOOT_PROMISE__, new Promise((_, reject) => {
            timeout = setTimeout(() => reject(new Error("JOA boot timeout")), 60000);
        })]);
        clearTimeout(timeout);
        const test = window.__nativeAdventureTest;
        assert(test, "Native test access missing");
        const records = ["A", "B"].map(id => ({id, folder:`Native Load ${id}`, name:`Native Load ${id}`,
            mode:"alekrytha", coverPolicy:"none", cover:""}));
        const keys = ["story", "characters", "locations", "groups", "mounts", "forgotten", "special"];
        for (const record of records) {
            test.register(record);
            await test.seed(record);
            for (const key of keys) {
                const result = await window.__alekGameRead(record.folder, key);
                assert(result?.ok && result.exists, `Seed missing ${record.id}/${key}`);
                result.data.__nativeMarker = `${record.id}/${key}`;
                const written = await window.__alekGameWrite(record.folder, key, result.data);
                assert(written?.ok, `Seed write failed ${record.id}/${key}`);
            }
        }
        const options = {persistCurrent:false, notice:false};
        await test.load(records[0], options);
        const old = test.snapshot(), oldSession = test.session();
        const pending = test.pendingModals();
        pending.seal.input.value = "test";
        assert(old.id === "A" && old.baselineId === "A", "A not active");
        const read = window.__alekGameRead;
        let rejected = false;
        window.__alekGameRead = (folder, key) => folder === records[1].folder && key === "special"
            ? Promise.resolve({ok:false, error:"Injected final document read failure"}) : read(folder, key);
        try { await test.load(records[1], options); }
        catch (error) { rejected = String(error).includes("Injected final document"); }
        finally { window.__alekGameRead = read; }
        assert(rejected, "Read failure was not propagated");
        const restored = test.snapshot();
        assert(restored.id === "A" && restored.registryId === "A" && restored.baselineId === "A", "Identity rollback failed");
        assert(restored.path === old.path, "Path rollback failed");
        restored.roots.forEach((root, index) => assert(root === old.roots[index], `Root replaced on failure: ${keys[index]}`));
        assert(test.session().ready && test.session().generation > oldSession.generation, "Session not recovered");
        assert(!pending.editor.shade.isConnected && !pending.seal.overlay.isConnected, "Stale modals survived failed load");
        assert(await pending.editor.save.onclick() === false, "Stale editor accepted save after rollback");
        assert(await pending.seal.overlay.querySelector(".confirm").onclick() === false, "Stale seal accepted confirmation");
        assert(!test.snapshot().roots[0].__nativeCallbackCount, "Old modal changed restored adventure");
        await test.load(records[1], options);
        const loaded = test.snapshot();
        assert(loaded.id === "B" && loaded.registryId === "B" && loaded.baselineId === "B", "Retry identity failed");
        loaded.roots.forEach((root, index) => assert(root.__nativeMarker === `B/${keys[index]}`, `Mixed document ${keys[index]}`));
        await test.load(records[0], options);
        test.snapshot().roots.forEach((root, index) => assert(root.__nativeMarker === `A/${keys[index]}`, `Return corrupted ${keys[index]}`));
        assert(await pending.editor.save.onclick() === false, "A-B-A revived old editor");
        const live = test.pendingModals();
        await Promise.all([live.editor.save.onclick(), live.editor.save.onclick()]);
        assert(test.snapshot().roots[0].__nativeCallbackCount === 1, "Duplicate editor commit");
        assert(!live.editor.shade.isConnected, "Successful editor stayed open");
        live.seal.input.value = "test";
        await live.seal.overlay.querySelector(".confirm").onclick();
        const persisted = await window.__alekGameRead(records[0].folder, "story");
        assert(persisted?.data?.__nativeCallbackCount === 2, "Current modal writes not persisted by Core");
        assert(!live.seal.overlay.isConnected, "Successful seal stayed open");
        const recordChecks = await window.__nativeRecordChecks(test, records);
        await window.__alekWriteJson("native-result.json", {ok:true, cases:4, callbackChecks:true, recordChecks, phase:"A → failed B → B → A; rename/create/delete"});
        window.__alekWriteText("tail.txt", "drained before exit");
        window.__alekAPI("app.exit", {});
    } catch (error) {
        clearTimeout(timeout);
        await window.__alekWriteJson("native-result.json", {ok:false, error:String(error.stack || error)});
        window.__alekAPI("app.exit", {});
    }
})();
