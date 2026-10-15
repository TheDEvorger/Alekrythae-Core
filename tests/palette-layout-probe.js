// Native layout regression: actual JOA styles/controller in isolated iframe viewports.
// No user adventure is opened or modified by these fixtures.
(async function testPaletteLayout() {
    const results = [];
    let frame;
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const read = async path => {
        const result = await window.__alekReadText(path);
        if (!result.ok) throw new Error(`${path}: ${result.error}`);
        return result.content;
    };
    try {
        await window.__ALEK_BOOT_PROMISE__;
        assert(window.__ALEK_ORCHESTRATOR__?.status.phase === "running", "JOA boot incomplete");
        const manifest = JSON.parse(await read("Alekrythae.App/manifest.json"));
        const paths = [...manifest.preloads, ...manifest.modules].filter(path =>
            !/^(legacy\/|modules\/(application|character-stats|journey-simulation)\/)/.test(path));
        const sources = [];
        for (const path of paths) sources.push(await read(`Alekrythae.App/${path}`));
        const styles = Array.from(document.styleSheets, sheet =>
            Array.from(sheet.cssRules, rule => rule.cssText).join("\n")).join("\n");
        frame = document.createElement("iframe");
        frame.style.cssText = "position:fixed;left:0;top:0;opacity:0;pointer-events:none;border:0;z-index:2147483646;background:#294542";
        document.body.appendChild(frame);
        const fixture = `
window.Alekrythae={registerModule:module=>(window.fixtureModules||=[]).push(module)};
window.Notice=()=>{};
`;
        const setup = `
window.paletteReady=(async()=>{
    const module=fixtureModules.find(item=>item.id==='joa.world-experience');
    await module.start({bridge:{},eventBus:{emit(){}}});
    const c=window.paletteController=module.controller;
    c.overlay=document.querySelector('.joa-world-shell');
    c.state={adventureId:'layout-fixture',layerKey:'surface',mapImages:[]};
    for(const name of ['scheduleSave','renderPaint','renderMapImages'])c[name]=()=>{};
    c.confirmRune=async()=>true;
    c.paletteOpen=true;c.paintMode=true;c.layerPanelOpen=true;
    const meta=c.currentPaintMeta();
    meta.layers['0'].name='Çok uzun bir katman adı — ışık ve kıyı çizgileri 🌙';
    c.renderPalette();
    const host=document.querySelector('.joa-floating-palette');
    host.addEventListener('click',event=>c.onPaletteClick(event));
    host.addEventListener('input',event=>c.onPaletteInput(event));
})();`;
        const script = source => `<script>${source.replace(/<\/script/gi, "<\\/script")}<\/script>`;
        frame.srcdoc = `<!doctype html><html lang="tr"><meta charset="utf-8"><style>${styles}</style>
<style>html,body{margin:0;overflow:hidden}.joa-world-shell{display:block;background:#294542}</style>
<section class="joa-world-shell"><aside class="joa-floating-palette"></aside></section>
${[fixture, ...sources, setup].map(script).join("")}</html>`;
        await new Promise(resolve => frame.addEventListener("load", resolve, {once:true}));
        await frame.contentWindow.paletteReady;
        const win = frame.contentWindow, c = win.paletteController;
        assert(c, "palette controller not initialized");
        const settle = () => new Promise(resolve => win.requestAnimationFrame(() => win.requestAnimationFrame(resolve)));
        const host = win.document.querySelector(".joa-floating-palette");
        const body = host.querySelector(".joa-palette-body");
        for (const [width, height] of [[1920,1080],[1024,768],[820,614],[683,512],[512,384],[320,568],[568,320]]) {
            frame.style.width = `${width}px`; frame.style.height = `${height}px`;
            for (const mode of ["compact", "brushes", "layers", "image"]) {
                c.brushPanelOpen = mode === "brushes";
                c.layerPanelOpen = mode === "layers" || mode === "image";
                const meta = c.currentPaintMeta();
                meta.layers[0].kind = mode === "image" ? "image" : "paint";
                c.state.paintPalettePosition = {left:9999,top:9999};
                c.renderPalette();
                host.querySelector(".joa-palette-handle button").focus();
                c.paletteView.position.schedule(c.state.paintPalettePosition);
                await settle();
                const box = host.getBoundingClientRect();
                const label = `${width}x${height}/${mode}`;
                assert(box.width > 0 && Math.abs(box.width - Math.min(300,width-16)) <= 1, `${label}: width ${box.width}`);
                assert(box.left >= 7 && box.top >= 7 && box.right <= width-7 && box.bottom <= height-7, `${label}: outside viewport ${JSON.stringify(box)}`);
                assert(body.scrollWidth <= body.clientWidth+1, `${label}: horizontal body overflow ${body.scrollWidth}/${body.clientWidth}`);
                for (const control of host.querySelectorAll("button,input")) {
                    if (!control.getClientRects().length) continue;
                    const rect = control.getBoundingClientRect();
                    assert(rect.left >= box.left && rect.right <= box.right+1, `${label}: control overflow ${control.outerHTML.slice(0,100)}`);
                }
                const header = host.querySelector(".joa-palette-handle");
                const headerTop = header.getBoundingClientRect().top;
                body.scrollTop = body.scrollHeight;
                assert(header.getBoundingClientRect().top === headerTop, `${label}: header scrolls away`);
                body.scrollTop = 0;
                results.push({width,height,mode,palette:{width:box.width,height:box.height},scrolls:body.scrollHeight>body.clientHeight});
            }
        }
        const meta = c.currentPaintMeta(); meta.layers[0].kind = "paint";
        c.layerPanelOpen=true;c.renderPalette();await settle();
        const input = host.querySelector('[data-role="brush-size"]');
        input.focus();
        for(let i=1;i<=100;i++) { input.value=String(i); input.dispatchEvent(new win.Event("input",{bubbles:true})); }
        assert(host.querySelector('[data-role="brush-size"]')===input && win.document.activeElement===input, "slider focus/identity changed");
        assert(c.paintBrush===100, "slider value lost");
        c.paletteView.dispose();frame.remove();frame=null;
        await window.__alekWriteJson("native-result.json", {ok:true,kind:"palette-layout",cases:results.length,results});
    } catch(error) {
        frame?.remove();
        await window.__alekWriteJson("native-result.json", {ok:false,error:String(error.stack||error),results});
    }
    window.__alekWriteText("tail.txt","drained before exit");
    window.__alekAPI("app.exit",{});
})();
