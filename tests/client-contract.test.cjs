"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const {spawn} = require("node:child_process");
const {once} = require("node:events");
const {createInterface} = require("node:readline");

const core = path.resolve(__dirname, "..");
const repo = path.resolve(core, "..");

test("production JS bridge and JOA journal round trip through actual Core SQLite", async t => {
    const exe = path.join(core, "tests/Core.Compatibility/bin/Release/net10.0-windows10.0.19041.0/win-x64/Core.Compatibility.exe");
    assert(fs.existsSync(exe), "Build Core.Compatibility first");
    const root = path.join(core, "test-output", `joa-${Date.now()}`);
    const child = spawn(exe, ["--bridge", root], {windowsHide: true, stdio: ["pipe", "pipe", "pipe"]});
    let errors = "";
    child.stderr.on("data", value => { errors += value; });
    const context = vm.createContext({console, setTimeout, clearTimeout});
    context.window = context;
    context.addEventListener = () => {};
    context.document = {createElement: () => ({}), head: {appendChild() {}}, body: {appendChild() {}}};
    context.chrome = {webview: {postMessage(value) {
        child.stdin.write((typeof value === "string" ? value : JSON.stringify(value)) + "\n");
    }}};
    const lines = createInterface({input: child.stdout});
    lines.on("line", line => {
        const response = JSON.parse(line);
        assert(!response.error, response.error);
        context.__alekDispatch(response.id, response.result);
    });
    t.after(async () => {
        const closed = once(child, "exit");
        child.stdin.end();
        const [code] = await closed;
        assert.equal(code, 0, errors);
        lines.close();
    });
    vm.runInContext(fs.readFileSync(path.join(core, "src/Alekrythae.Core/Bridge/client.js"), "utf8"), context);
    assert.equal(context.__alekSave(JSON.stringify({legacy: "ışık 🌙"})), true);
    assert.equal((await context.__alekReadJson("fixture.alek.json")).data.legacy, "ışık 🌙");
    assert.equal((await context.__alekWriteJson("Data/bridge.json", {value: 42})).ok, true);
    assert.equal((await context.__alekReadJson("Data/bridge.json")).data.value, 42);
    assert.equal((await context.__alekReadText("../escape")).error, "invalid_path");
    assert.equal((await context.__alekGameEnsure("A", "real_world")).ok, true);
    assert.equal((await context.__alekGameEnsure("B", "other_realm")).ok, true);
    require(path.join(repo, "Alekrythae.App/modules/adventures/persistence/atomic-documents.js"));
    let failProjection = false;
    const api = {
        read: (folder, key) => context.__alekGameRead(folder, key),
        write: (folder, key, data) => failProjection && key === "world"
            ? Promise.resolve({ok: false, error: "injected projection failure"})
            : context.__alekGameWrite(folder, key, data)
    };
    let store = globalThis.AlekrythaeDocumentStore.create(api);
    assert.equal((await store.writeBatch("A", [
        {key: "characters", data: [{id: "a", containerKey: "root"}]},
        {key: "world", data: {paint: [{color: "#00eaff", points: [[1, 2], [3, 4]]}]}}
    ])).ok, true);
    await store.write("B", "world", {name: "Other adventure"});
    failProjection = true;
    const saved = await store.writeBatch("A", [
        {key: "characters", data: [{id: "a", containerKey: "character:b"}]},
        {key: "world", data: {paint: [], label: "Kurtarılan kayıt"}}
    ]);
    assert.equal(saved.ok, true);
    assert.equal(saved.projected, false);
    // Discard all JOA journal state; every Core request opens/closes the real database.
    store = globalThis.AlekrythaeDocumentStore.create(api);
    assert.equal((await store.read("A", "world")).data.label, "Kurtarılan kayıt");
    assert.equal((await store.read("A", "characters")).data[0].containerKey, "character:b");
    assert.equal((await store.read("B", "world")).data.name, "Other adventure");
    failProjection = false;
    assert.equal(await store.flush("A"), true);
    assert.equal((await context.__alekGameRead("A", "world")).data.label, "Kurtarılan kayıt");
    assert.equal(Object.keys(context.__alekPending).length, 0, "unresolved bridge promises");
});
