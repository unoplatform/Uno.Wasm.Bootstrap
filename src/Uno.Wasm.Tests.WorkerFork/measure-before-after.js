"use strict";
var puppeteer = require("puppeteer");
var fs = require("fs");

function getProcessSmaps(pid) {
    try {
        var rollup = fs.readFileSync("/proc/" + pid + "/smaps_rollup", "utf8");
        var rss = rollup.match(/Rss:\s+(\d+)\s+kB/);
        var pss = rollup.match(/Pss:\s+(\d+)\s+kB/);
        var priv = rollup.match(/Private_Clean:\s+(\d+)\s+kB/);
        var privDirty = rollup.match(/Private_Dirty:\s+(\d+)\s+kB/);
        return {
            rss_kb: rss ? parseInt(rss[1]) : 0,
            pss_kb: pss ? parseInt(pss[1]) : 0,
            private_kb: (priv ? parseInt(priv[1]) : 0) + (privDirty ? parseInt(privDirty[1]) : 0),
        };
    } catch (e) { return null; }
}

(async function () {
    var browser = await puppeteer.launch({
        headless: true,
        args: ["--no-sandbox", "--disable-setuid-sandbox", "--enable-precise-memory-info"],
    });

    async function getRendererPid() {
        try {
            var cdp = await browser.target().createCDPSession();
            var info = await cdp.send("SystemInfo.getProcessInfo");
            await cdp.detach();
            var renderer = null;
            info.processInfo.forEach(function (p) {
                if (p.type === "renderer") {
                    var smaps = getProcessSmaps(p.id);
                    if (smaps && (!renderer || smaps.private_kb > renderer.private_kb)) {
                        renderer = { pid: p.id, smaps: smaps };
                    }
                }
            });
            return renderer;
        } catch (e) { return null; }
    }

    var page = await browser.newPage();

    // Intercept to block worker creation — we'll measure before the worker starts.
    // We inject a flag to pause before forking.
    var workerReady = false;
    var mainReady = false;
    page.on("console", function (msg) {
        var text = msg.text();
        if (text.indexOf("worker is ready") !== -1) workerReady = true;
        if (text.indexOf("Main thread: runtime started") !== -1) mainReady = true;
        if (text.indexOf("[Worker]") !== -1 || text.indexOf("Main thread") !== -1 ||
            text.indexOf("forkToWorker") !== -1 || text.indexOf("WASM") !== -1) {
            console.log("  BROWSER: " + text);
        }
    });

    console.log("=== Before/After Worker Memory Measurement ===\n");

    // Step 1: Load app but inject a pause before forkToWorker
    // We'll use page.evaluateOnNewDocument to override forkToWorker to wait for a signal.
    var forkPaused = true;
    await page.evaluateOnNewDocument(function () {
        // Create a gate that blocks forkToWorker until we release it.
        window.__forkGate = new Promise(function (resolve) {
            window.__releaseFork = resolve;
        });
        // Monkey-patch: wrap the real forkToWorker once Uno namespace is available.
        var origDefProp = Object.defineProperty;
        var patched = false;
        // Poll for it.
        var timer = setInterval(function () {
            try {
                if (globalThis.Uno && globalThis.Uno.WebAssembly &&
                    globalThis.Uno.WebAssembly.Bootstrap &&
                    globalThis.Uno.WebAssembly.Bootstrap.WorkerFork &&
                    !patched) {
                    patched = true;
                    var origFork = globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.forkToWorker;
                    globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.forkToWorker = async function (opts) {
                        console.log("forkToWorker: PAUSED (waiting for gate)");
                        await window.__forkGate;
                        console.log("forkToWorker: RELEASED, proceeding");
                        return origFork.call(this, opts);
                    };
                    clearInterval(timer);
                }
            } catch (_e) {}
        }, 50);
    });

    console.log("Loading app...");
    await page.goto("http://localhost:8028/", { waitUntil: "load", timeout: 120000 });

    // Wait for main thread runtime to be ready (before fork).
    var retries = 60;
    while (!mainReady && retries-- > 0) {
        await new Promise(function (r) { setTimeout(r, 1000); });
    }

    // Extra settle time + GC.
    await new Promise(function (r) { setTimeout(r, 3000); });
    var cdp = await page.target().createCDPSession();
    await cdp.send("HeapProfiler.collectGarbage");
    await cdp.detach();
    await new Promise(function (r) { setTimeout(r, 2000); });

    // BEFORE measurement.
    var beforeMetrics = await page.metrics();
    var beforeRenderer = await getRendererPid();
    var beforeWasm = await page.evaluate(function () {
        var result = {};
        if (globalThis.Module && globalThis.Module.HEAPU8) {
            result.mainWasmHeap = globalThis.Module.HEAPU8.byteLength;
        }
        return result;
    });

    console.log("\n--- BEFORE worker fork ---");
    console.log("  JS Heap Used:  " + (beforeMetrics.JSHeapUsedSize / 1024 / 1024).toFixed(2) + " MB");
    console.log("  JS Heap Total: " + (beforeMetrics.JSHeapTotalSize / 1024 / 1024).toFixed(2) + " MB");
    if (beforeWasm.mainWasmHeap) {
        console.log("  WASM linear memory: " + (beforeWasm.mainWasmHeap / 1024 / 1024).toFixed(1) + " MB");
    }
    if (beforeRenderer) {
        console.log("  Renderer RSS:     " + (beforeRenderer.smaps.rss_kb / 1024).toFixed(1) + " MB");
        console.log("  Renderer PSS:     " + (beforeRenderer.smaps.pss_kb / 1024).toFixed(1) + " MB");
        console.log("  Renderer Private: " + (beforeRenderer.smaps.private_kb / 1024).toFixed(1) + " MB");
    }

    // Release the fork gate.
    console.log("\n--- Releasing worker fork ---");
    await page.evaluate(function () { window.__releaseFork(); });

    // Wait for worker to become ready.
    retries = 60;
    while (!workerReady && retries-- > 0) {
        await new Promise(function (r) { setTimeout(r, 1000); });
    }
    console.log("  Worker ready: " + workerReady);

    // Settle + GC.
    await new Promise(function (r) { setTimeout(r, 5000); });
    cdp = await page.target().createCDPSession();
    await cdp.send("HeapProfiler.collectGarbage");
    await cdp.detach();
    await new Promise(function (r) { setTimeout(r, 2000); });

    // AFTER measurement.
    var afterMetrics = await page.metrics();
    var afterRenderer = await getRendererPid();

    console.log("\n--- AFTER worker fork ---");
    console.log("  JS Heap Used:  " + (afterMetrics.JSHeapUsedSize / 1024 / 1024).toFixed(2) + " MB");
    console.log("  JS Heap Total: " + (afterMetrics.JSHeapTotalSize / 1024 / 1024).toFixed(2) + " MB");
    if (afterRenderer) {
        console.log("  Renderer RSS:     " + (afterRenderer.smaps.rss_kb / 1024).toFixed(1) + " MB");
        console.log("  Renderer PSS:     " + (afterRenderer.smaps.pss_kb / 1024).toFixed(1) + " MB");
        console.log("  Renderer Private: " + (afterRenderer.smaps.private_kb / 1024).toFixed(1) + " MB");
    }

    // Delta.
    console.log("\n--- DELTA (worker cost) ---");
    if (beforeRenderer && afterRenderer) {
        var deltaRss = (afterRenderer.smaps.rss_kb - beforeRenderer.smaps.rss_kb) / 1024;
        var deltaPss = (afterRenderer.smaps.pss_kb - beforeRenderer.smaps.pss_kb) / 1024;
        var deltaPriv = (afterRenderer.smaps.private_kb - beforeRenderer.smaps.private_kb) / 1024;
        console.log("  RSS increase:     " + (deltaRss > 0 ? "+" : "") + deltaRss.toFixed(1) + " MB");
        console.log("  PSS increase:     " + (deltaPss > 0 ? "+" : "") + deltaPss.toFixed(1) + " MB");
        console.log("  Private increase: " + (deltaPriv > 0 ? "+" : "") + deltaPriv.toFixed(1) + " MB");
    }
    var deltaHeapUsed = (afterMetrics.JSHeapUsedSize - beforeMetrics.JSHeapUsedSize) / 1024 / 1024;
    console.log("  Main JS Heap delta: " + (deltaHeapUsed > 0 ? "+" : "") + deltaHeapUsed.toFixed(2) + " MB");

    // E2E result check.
    var results = null;
    try { results = await page.$eval("#results", function (el) { return el.textContent; }); } catch (e) {}
    console.log("\n  E2E result: " + (results || "(none)"));

    console.log("\n--- Analysis ---");
    console.log("  Module reuse is currently DISABLED.");
    console.log("  The worker downloads and compiles its own dotnet.native.wasm (~8MB).");
    console.log("  Private memory increase ≈ cost of a second .NET runtime instance.");
    console.log("  With module reuse ENABLED, the compiled WASM code (~15-25MB) would be shared,");
    console.log("  reducing the per-worker overhead significantly.");

    await browser.close();
    console.log("\nDone.");
    process.exit(0);
})().catch(function (err) {
    console.error("ERROR: " + err);
    process.exit(1);
});
