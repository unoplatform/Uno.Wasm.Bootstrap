"use strict";
var puppeteer = require("puppeteer");
var fs = require("fs");

function getProcessSmaps(pid) {
    try {
        var rollup = fs.readFileSync("/proc/" + pid + "/smaps_rollup", "utf8");
        var rss = rollup.match(/Rss:\s+(\d+)\s+kB/);
        var pss = rollup.match(/Pss:\s+(\d+)\s+kB/);
        var shared = rollup.match(/Shared_Clean:\s+(\d+)\s+kB/);
        var sharedDirty = rollup.match(/Shared_Dirty:\s+(\d+)\s+kB/);
        var priv = rollup.match(/Private_Clean:\s+(\d+)\s+kB/);
        var privDirty = rollup.match(/Private_Dirty:\s+(\d+)\s+kB/);
        return {
            rss_kb: rss ? parseInt(rss[1]) : 0,
            pss_kb: pss ? parseInt(pss[1]) : 0,
            shared_kb: (shared ? parseInt(shared[1]) : 0) + (sharedDirty ? parseInt(sharedDirty[1]) : 0),
            private_kb: (priv ? parseInt(priv[1]) : 0) + (privDirty ? parseInt(privDirty[1]) : 0),
        };
    } catch (e) { return null; }
}

(async function () {
    var browser = await puppeteer.launch({
        headless: true,
        args: [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--enable-precise-memory-info",
            // Required for performance.measureUserAgentSpecificMemory()
            "--enable-features=PerformanceManagerInstrumentation",
        ],
    });
    var page = await browser.newPage();

    var workerReady = false;
    page.on("console", function (msg) {
        var text = msg.text();
        if (text.indexOf("worker is ready") !== -1) workerReady = true;
    });

    async function getProcessInfo() {
        try {
            var cdp = await browser.target().createCDPSession();
            var info = await cdp.send("SystemInfo.getProcessInfo");
            await cdp.detach();
            return info.processInfo;
        } catch (e) { return null; }
    }

    function measureProcesses(procs) {
        var results = [];
        var rendererSmaps = null;
        procs.forEach(function (p) {
            var smaps = getProcessSmaps(p.id);
            if (smaps) {
                results.push({ pid: p.id, type: p.type, smaps: smaps });
                if (p.type === "renderer" && (!rendererSmaps || smaps.private_kb > rendererSmaps.private_kb)) {
                    rendererSmaps = smaps;
                }
            }
        });
        return { all: results, renderer: rendererSmaps };
    }

    console.log("=== Memory Measurement: Main Thread vs Worker ===\n");

    // --- Load the app ---
    console.log("Loading app...");
    await page.goto("http://localhost:8028/", { waitUntil: "load", timeout: 120000 });

    // Wait for runtime + worker
    var retries = 60;
    while (!workerReady && retries-- > 0) {
        await new Promise(function (r) { setTimeout(r, 1000); });
    }
    console.log("Worker ready: " + workerReady);

    // Let memory settle
    await new Promise(function (r) { setTimeout(r, 5000); });

    // Force GC
    var cdp = await page.target().createCDPSession();
    await cdp.send("HeapProfiler.collectGarbage");
    await cdp.detach();
    await new Promise(function (r) { setTimeout(r, 2000); });

    // --- Measurement 1: performance.measureUserAgentSpecificMemory() ---
    var memBreakdown = null;
    try {
        memBreakdown = await page.evaluate(async function () {
            if (typeof performance.measureUserAgentSpecificMemory === "function") {
                return await performance.measureUserAgentSpecificMemory();
            }
            return null;
        });
    } catch (e) {
        console.log("measureUserAgentSpecificMemory not available: " + e.message);
    }

    // --- Measurement 2: page.metrics() for main thread heap ---
    var metrics = await page.metrics();

    // --- Measurement 3: Renderer process from /proc ---
    var procs = await getProcessInfo();
    var procMem = measureProcesses(procs);

    // --- Measurement 4: Check WASM memory from inside the page ---
    var wasmMemory = await page.evaluate(function () {
        var result = {};
        // Check Module's WASM memory
        if (globalThis.Module && globalThis.Module.HEAPU8) {
            result.mainWasmHeap = globalThis.Module.HEAPU8.byteLength;
        }
        // Check if __unoWasmModule exists
        if (globalThis.__unoWasmModule) {
            result.hasSharedModule = true;
            result.moduleImports = WebAssembly.Module.imports(globalThis.__unoWasmModule).length;
            result.moduleExports = WebAssembly.Module.exports(globalThis.__unoWasmModule).length;
        }
        return result;
    });

    // --- OUTPUT ---
    console.log("\n========================================");
    console.log("   MEMORY BREAKDOWN");
    console.log("========================================\n");

    // measureUserAgentSpecificMemory breakdown
    if (memBreakdown) {
        console.log("--- performance.measureUserAgentSpecificMemory() ---");
        console.log("  Total bytes: " + (memBreakdown.bytes / 1024 / 1024).toFixed(2) + " MB");
        if (memBreakdown.breakdown) {
            memBreakdown.breakdown.forEach(function (entry) {
                if (entry.bytes > 0) {
                    var types = entry.types ? entry.types.join(", ") : "unknown";
                    var scope = "unknown";
                    if (entry.attribution && entry.attribution.length > 0) {
                        scope = entry.attribution.map(function (a) {
                            if (a.scope) return a.scope;
                            if (a.url) return a.url.substring(a.url.lastIndexOf("/") + 1);
                            return "unknown";
                        }).join(", ");
                    }
                    console.log("  " + (entry.bytes / 1024 / 1024).toFixed(2) + " MB  [" + types + "]  scope=" + scope);
                }
            });
        }
    } else {
        console.log("--- measureUserAgentSpecificMemory: NOT AVAILABLE ---");
        console.log("  (Requires COEP headers, which our server provides)");
    }

    // page.metrics
    console.log("\n--- page.metrics() (main thread only) ---");
    console.log("  JS Heap Used:  " + (metrics.JSHeapUsedSize / 1024 / 1024).toFixed(2) + " MB");
    console.log("  JS Heap Total: " + (metrics.JSHeapTotalSize / 1024 / 1024).toFixed(2) + " MB");

    // WASM memory
    console.log("\n--- WebAssembly Memory ---");
    if (wasmMemory.mainWasmHeap) {
        console.log("  Main thread WASM linear memory: " + (wasmMemory.mainWasmHeap / 1024 / 1024).toFixed(1) + " MB");
    }
    if (wasmMemory.hasSharedModule) {
        console.log("  __unoWasmModule captured: yes (" + wasmMemory.moduleImports + " imports, " + wasmMemory.moduleExports + " exports)");
    }

    // Renderer process
    console.log("\n--- Renderer Process (main + worker) ---");
    if (procMem.renderer) {
        var r = procMem.renderer;
        console.log("  RSS:     " + (r.rss_kb / 1024).toFixed(1) + " MB");
        console.log("  PSS:     " + (r.pss_kb / 1024).toFixed(1) + " MB");
        console.log("  Shared:  " + (r.shared_kb / 1024).toFixed(1) + " MB");
        console.log("  Private: " + (r.private_kb / 1024).toFixed(1) + " MB");
    }

    // All processes
    console.log("\n--- All Chromium Processes ---");
    procMem.all.forEach(function (p) {
        var s = p.smaps;
        console.log("  " + p.type + " (PID " + p.pid + "): RSS=" + (s.rss_kb / 1024).toFixed(1) + " MB  private=" + (s.private_kb / 1024).toFixed(1) + " MB  shared=" + (s.shared_kb / 1024).toFixed(1) + " MB");
    });

    // Summary
    console.log("\n--- Summary ---");
    console.log("  The worker runs in the same Chromium renderer process.");
    console.log("  With module reuse DISABLED, each instance has its own compiled WASM.");
    if (procMem.renderer) {
        var priv = procMem.renderer.private_kb / 1024;
        console.log("  Renderer private memory: " + priv.toFixed(1) + " MB (both main + worker)");
        console.log("  Estimated per-instance: ~" + (priv / 2).toFixed(0) + " MB each");
        console.log("  With module sharing enabled, expect ~15-25 MB savings from");
        console.log("  avoiding duplicate WASM compilation.");
    }

    // Results
    var results = null;
    try { results = await page.$eval("#results", function (el) { return el.textContent; }); } catch (e) {}
    console.log("\n  E2E result: " + (results || "(none)"));

    await browser.close();
    console.log("\nDone.");
    process.exit(0);
})().catch(function (err) {
    console.error("ERROR: " + err);
    process.exit(1);
});
