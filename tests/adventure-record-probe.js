// Loaded only by the isolated native test runner, before adventure-load-probe.js.
window.__nativeRecordChecks = async (test, records) => {
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const oldOwner = test.session(), nativeMove = window.__alekGameRename;
    let release, started;
    const gate = new Promise(resolve => { release = resolve; });
    const moving = new Promise(resolve => { started = resolve; });
    window.__alekGameRename = async (from, to) => {
        started(); await gate; return nativeMove(from, to);
    };
    test.snapshot().roots[0].__nativeRecordMarker = "saved before rename";
    try {
        const rename = test.rename("A", "Native Renamed A");
        await moving;
        assert(!test.session().ready && await test.save() === false, "Rename allowed concurrent save");
        const load = test.load(records[1], {persistCurrent:false, notice:false});
        await Promise.resolve();
        assert(test.snapshot().id === "A", "Load bypassed pending rename");
        release();
        assert(await rename === true && await load === true, "Rename/load failed");
    } finally { release(); window.__alekGameRename = nativeMove; }
    assert(records[0].folder === "Native Renamed A", "Rename did not update canonical record");
    await test.load(records[0], {persistCurrent:false, notice:false});
    const renamed = await window.__alekGameRead(records[0].folder, "story");
    assert(renamed?.data?.__nativeRecordMarker === "saved before rename", "Rename lost the latest SQLite save");
    assert(await test.remove("A", oldOwner) === false, "Old confirmation survived rename and return");
    const created = await test.add({id:"C", folder:"Native Created C", name:"Native Created C",
        mode:"alekrytha", coverPolicy:"none", cover:""}, {year:24000, month:1, day:1, hour:8, minute:0});
    assert(created?.id === "C", "Creation failed");
    assert(await test.load(created, {persistCurrent:false, notice:false}) === true, "Created record not loadable");
    assert(await test.remove("C") === true, "Active deletion failed");
    assert(test.snapshot().id !== "C" && test.session().ready, "Deleted runtime stayed active");
    assert(await test.load(created, {persistCurrent:false, notice:false}) === false, "Stale card recreated deleted record");
    const registry = await window.__alekRegistryRead();
    assert(registry?.ok && !registry.data.adventures.some(record => record.id === "C"), "Deleted record remains in registry");
    assert(registry.data.adventures.some(record => record.id === "A" && record.folder === "Native Renamed A"), "Rename registry lost");
    return true;
};
