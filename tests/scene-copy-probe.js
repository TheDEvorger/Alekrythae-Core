// Real mounted Tavern/F3 controls in Core. Intercept copy command; never touch the OS clipboard.
window.__sceneCopyAcceptance = async () => {
    const assert = (value, message) => {
        if (!value) throw new Error(message);
    };
    const access = window.__nativeSceneAcceptance;
    const capability = {
        secureContext: window.isSecureContext,
        writeTextAvailable: typeof navigator.clipboard?.writeText === "function",
        execCommandAvailable: typeof document.execCommand === "function",
    };
    assert(!capability.writeTextAvailable, "This Core fixture must exercise the missing async clipboard path.");
    assert(capability.execCommandAvailable, "Core copy command unavailable.");
    const original = Object.getOwnPropertyDescriptor(document, "execCommand");
    const probe = document.createElement("textarea");
    probe.value = "Türkçe seçim 🌙";
    probe.style.cssText = "position:fixed;left:0;top:0;width:1px;height:1px;opacity:0";
    document.body.append(probe);
    const rows = [];
    let mode = "success",
        captured,
        temporary;
    Object.defineProperty(document, "execCommand", {
        configurable: true,
        value: (command) => {
            temporary = document.activeElement;
            assert(command === "copy" && temporary?.tagName === "TEXTAREA", "Copy field not selected.");
            assert(temporary !== probe && temporary.readOnly, "Copy uses its own read-only field.");
            assert(
                temporary.selectionStart === 0 && temporary.selectionEnd === temporary.value.length,
                "Copy selects the whole payload.",
            );
            captured = temporary.value;
            if (mode === "throw") throw new Error("Fixture clipboard busy");
            return mode === "success";
        },
    });
    try {
        const messages = Array.from({ length: 1100 }, (_, index) => ({
            id: "clipboard-" + index,
            year: 1,
            month: 1,
            day: 1,
            order: index,
            speakerType: "meggy",
            speakerName: "Meggy",
            speakerTitle: "",
            text: "Türkçe 🌙 mesaj " + index,
            timeStartedAt: "08:000",
            createdAt: new Date(1700000000000 + index * 1000).toISOString(),
        }));
        for (const view of ["encounter", "ai"]) {
            await access.render(view, true, messages);
            const selector =
                view === "ai" ? ".joa-event-log .joa-event-copy-controls" : ".tavern-stage .joa-event-copy-controls";
            let controls;
            for (let turn = 0; turn < 200; turn++) {
                controls = document.querySelector(selector);
                if (controls) break;
                await new Promise((resolve) => setTimeout(resolve, 30));
            }
            assert(controls, "Mounted copy controls unavailable: " + view);
            const entries = await access.copyEntries(view);
            if (view === "ai")
                assert(entries.length > messages.length, "Saved gameplay events missing from journal fixture.");
            const buttons = [...controls.querySelectorAll("button")];
            for (const [index, count] of [10, 100, 1000].entries()) {
                probe.focus({ preventScroll: true });
                probe.setSelectionRange(2, 6, "backward");
                await buttons[index].onclick();
                assert(
                    captured === window.AlekrythaeEventLogView.format(entries, count),
                    "Copy payload mismatch: " + view + "/" + count,
                );
                assert(
                    controls.querySelector("small").textContent === count + " kayıt kopyalandı.",
                    "Copy status mismatch.",
                );
                assert(
                    !temporary.isConnected && document.activeElement === probe,
                    "Copy leaks a field or loses focus.",
                );
                assert(
                    probe.selectionStart === 2 && probe.selectionEnd === 6 && probe.selectionDirection === "backward",
                    "Copy changes the composer selection.",
                );
                rows.push({ view, count, exactUnicodePayload: true, focusAndSelectionRestored: true });
            }
            for (mode of ["false", "throw"]) {
                await buttons[0].onclick();
                assert(
                    controls.querySelector("small").textContent === "Panoya kopyalanamadı.",
                    "Failed copy reports success.",
                );
                assert(!temporary.isConnected, "Failed copy leaves the temporary field.");
            }
            mode = "success";
            await buttons[0].onclick();
            assert(controls.querySelector("small").textContent === "10 kayıt kopyalandı.", "Copy retry fails.");
        }
        return {
            capability,
            rows,
            failedCopyAndRetry: true,
            commandIntercepted: true,
            osClipboardRead: false,
            osClipboardWritten: false,
            trustedInput: false,
        };
    } finally {
        probe.remove();
        if (original) Object.defineProperty(document, "execCommand", original);
        else delete document.execCommand;
    }
};
