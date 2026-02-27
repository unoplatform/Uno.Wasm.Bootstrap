"use strict";

/**
 * Launches headless Chrome via Puppeteer, loads the published RayTracer app
 * (served at BOOTSTRAP_TEST_RUNNER_URL), waits for the memory profiler class
 * to be available, then calls downloadSnapshot for speedscope and PerfView
 * formats.  The captured JSON is validated and written as artifact files.
 *
 * Usage:  node validate-export-formats.js <output-dir> [app-url]
 */

var puppeteer = require("puppeteer");
var fs = require("fs");
var path = require("path");

var outputDir = process.argv[2] || "./artifacts";
var appUrl = process.argv[3] || process.env.BOOTSTRAP_TEST_RUNNER_URL || "http://localhost:8000/";

fs.mkdirSync(outputDir, { recursive: true });

var failures = 0;
function assert(condition, message) {
    if (!condition) {
        console.error("  FAIL: " + message);
        failures++;
    }
}

// Mock allocation data in Emscripten profiler format (V8 stack traces).
// Used only when the Emscripten native profiler is not linked in (no real data).
var MOCK_ALLOCATIONS = {};
// Site 1: 65536 bytes outstanding (largest)
MOCK_ALLOCATIONS[
    "at dotnet.native.wasm.dlmalloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[100]:0x1000)\n" +
    "at dotnet.native.wasm.internal_memalign (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[101]:0x2000)\n" +
    "at dotnet.native.wasm.sgen_card_table_init (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[200]:0x3000)"
] = [5, 65536, ""];
// Site 2: 32768 bytes outstanding
MOCK_ALLOCATIONS[
    "at dotnet.native.wasm.dlmalloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[100]:0x1000)\n" +
    "at dotnet.native.wasm.monoeg_malloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[102]:0x4000)\n" +
    "at dotnet.native.wasm.mono_gc_alloc_obj (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[300]:0x5000)"
] = [3, 32768, ""];
// Site 3: 16384 bytes outstanding (smallest)
MOCK_ALLOCATIONS[
    "at dotnet.native.wasm.dlmalloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[100]:0x1000)\n" +
    "at dotnet.native.wasm.custom_alloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[400]:0x6000)"
] = [1, 16384, ""];

function delay(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, ms); });
}

/**
 * Calls downloadSnapshot inside the page, intercepting the Blob to capture
 * the JSON string instead of triggering a real file download.
 */
function captureSnapshot(page, format) {
    return page.evaluate(function (fmt) {
        var captured = null;
        var OrigBlob = globalThis.Blob;

        // Override Blob to capture the JSON content
        globalThis.Blob = function CaptureBlob(parts, options) {
            captured = parts.join("");
            return new OrigBlob(parts, options);
        };
        // Inherit prototype so instanceof checks pass
        globalThis.Blob.prototype = OrigBlob.prototype;

        // Override URL methods to avoid real object URL creation
        var origCreate = window.URL.createObjectURL;
        var origRevoke = window.URL.revokeObjectURL;
        window.URL.createObjectURL = function () { return "blob:captured"; };
        window.URL.revokeObjectURL = function () {};

        // Override anchor click to prevent actual download
        var origCreateElement = document.createElement.bind(document);
        document.createElement = function (tag) {
            var el = origCreateElement(tag);
            if (tag === "a") {
                el.click = function () {};
            }
            return el;
        };

        Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.downloadSnapshot(fmt);

        // Restore
        globalThis.Blob = OrigBlob;
        window.URL.createObjectURL = origCreate;
        window.URL.revokeObjectURL = origRevoke;
        document.createElement = origCreateElement;

        return captured;
    }, format);
}

(function () {
    var _run = async function () {
        console.log("Launching headless browser...");
        var browser = await puppeteer.launch({
            headless: true,
            args: ["--no-sandbox", "--disable-setuid-sandbox"],
            defaultViewport: { width: 1280, height: 1024 },
        });

        var page = await browser.newPage();

        page.on("console", function (msg) {
            console.log("BROWSER: " + msg.text());
        });
        page.on("pageerror", function (err) {
            console.error("PAGE ERROR: " + err.message);
        });

        console.log("Navigating to " + appUrl);
        await page.goto(appUrl, { waitUntil: "load", timeout: 120000 });

        // Wait for the Bootstrap namespace and EmscriptenMemoryProfilerSupport class
        console.log("Waiting for EmscriptenMemoryProfilerSupport...");
        await page.waitForFunction(
            "!!(globalThis.Uno" +
            " && globalThis.Uno.WebAssembly" +
            " && globalThis.Uno.WebAssembly.Bootstrap" +
            " && globalThis.Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport" +
            " && typeof globalThis.Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.downloadSnapshot === 'function')",
            { timeout: 120000 }
        );
        console.log("EmscriptenMemoryProfilerSupport is available.");

        // Wait up to 30s for real Emscripten profiler data (allocations with outstanding > 0)
        var hasRealData = false;
        try {
            await page.waitForFunction(
                "(function() {" +
                "  var p = globalThis.emscriptenMemoryProfiler;" +
                "  if (!p || !p.allocationsAtLoc) return false;" +
                "  var keys = Object.keys(p.allocationsAtLoc);" +
                "  for (var i = 0; i < keys.length; i++) {" +
                "    if (p.allocationsAtLoc[keys[i]][0] > 0) return true;" +
                "  }" +
                "  return false;" +
                "})()",
                { timeout: 30000 }
            );
            hasRealData = true;
        } catch (_e) {
            // timeout — no real profiler data
        }

        if (hasRealData) {
            console.log("Found real Emscripten profiler data — using live allocation data.");
        } else {
            console.log("No live profiler data (Emscripten --memoryprofiler may not be linked).");
            console.log("Injecting representative allocation data...");
            await page.evaluate(function (mockData) {
                if (!globalThis.emscriptenMemoryProfiler) {
                    globalThis.emscriptenMemoryProfiler = { updateUi: function () {} };
                }
                globalThis.emscriptenMemoryProfiler.allocationsAtLoc = mockData;
            }, MOCK_ALLOCATIONS);
        }

        // -----------------------------------------------------------------
        // Speedscope format
        // -----------------------------------------------------------------
        console.log("\n=== Validating speedscope format ===");

        var speedscopeRaw = await captureSnapshot(page, "speedscope");
        assert(speedscopeRaw !== null, "downloadSnapshot('speedscope') should produce output");

        var speedscope;
        try {
            speedscope = JSON.parse(speedscopeRaw);
        } catch (e) {
            console.error("  FAIL: speedscope output is not valid JSON: " + e.message);
            await browser.close();
            process.exit(1);
        }

        var speedscopeOutPath = path.join(outputDir, "test-output.speedscope.json");
        fs.writeFileSync(speedscopeOutPath, JSON.stringify(speedscope, null, 2));
        console.log("  Written: " + speedscopeOutPath);

        assert(
            speedscope.$schema === "https://www.speedscope.app/file-format-schema.json",
            "$schema should be speedscope URL, got: " + speedscope.$schema
        );

        assert(
            speedscope.shared && Array.isArray(speedscope.shared.frames),
            "shared.frames should be an array"
        );
        var frames = speedscope.shared.frames;
        console.log("  shared.frames: " + frames.length + " entries");
        assert(frames.length > 0, "shared.frames should not be empty");

        assert(
            Array.isArray(speedscope.profiles) && speedscope.profiles.length > 0,
            "profiles should be a non-empty array"
        );
        var profile = speedscope.profiles[0];
        assert(profile.type === "sampled", "profile.type should be 'sampled', got: " + profile.type);
        assert(profile.unit === "bytes", "profile.unit should be 'bytes', got: " + profile.unit);

        assert(Array.isArray(profile.samples), "profile.samples should be an array");
        assert(Array.isArray(profile.weights), "profile.weights should be an array");
        assert(
            profile.samples.length === profile.weights.length,
            "samples.length (" + profile.samples.length + ") should equal weights.length (" + profile.weights.length + ")"
        );
        assert(profile.samples.length > 0, "samples should not be empty");

        // Validate frame indices
        for (var i = 0; i < profile.samples.length; i++) {
            var sample = profile.samples[i];
            assert(Array.isArray(sample), "samples[" + i + "] should be an array");
            for (var j = 0; j < sample.length; j++) {
                assert(typeof sample[j] === "number", "frame index should be a number");
                assert(
                    sample[j] >= 0 && sample[j] < frames.length,
                    "frame index " + sample[j] + " out of range [0, " + frames.length + ")"
                );
            }
        }

        // Validate weights are positive
        for (var i = 0; i < profile.weights.length; i++) {
            assert(typeof profile.weights[i] === "number", "weights[" + i + "] should be a number");
            assert(profile.weights[i] >= 0, "weights[" + i + "] should be >= 0, got " + profile.weights[i]);
        }

        // endValue should equal sum of weights
        var weightSum = profile.weights.reduce(function (a, b) { return a + b; }, 0);
        assert(
            profile.endValue === weightSum,
            "endValue (" + profile.endValue + ") should equal sum of weights (" + weightSum + ")"
        );

        // Sorting: first weight should be the largest (sorted by bytes desc)
        for (var i = 1; i < profile.weights.length; i++) {
            assert(
                profile.weights[i - 1] >= profile.weights[i],
                "weights should be sorted descending: weights[" + (i - 1) + "]=" + profile.weights[i - 1] + " < weights[" + i + "]=" + profile.weights[i]
            );
        }

        console.log("  speedscope validation complete.");

        // -----------------------------------------------------------------
        // PerfView format
        // -----------------------------------------------------------------
        console.log("\n=== Validating PerfView format ===");

        var perfviewRaw = await captureSnapshot(page, "perfview");
        assert(perfviewRaw !== null, "downloadSnapshot('perfview') should produce output");

        var perfview;
        try {
            perfview = JSON.parse(perfviewRaw);
        } catch (e) {
            console.error("  FAIL: PerfView output is not valid JSON: " + e.message);
            await browser.close();
            process.exit(1);
        }

        var perfviewOutPath = path.join(outputDir, "test-output.PerfView.json");
        fs.writeFileSync(perfviewOutPath, JSON.stringify(perfview, null, 2));
        console.log("  Written: " + perfviewOutPath);

        assert(Array.isArray(perfview.Samples), "Samples should be an array");
        assert(perfview.Samples.length > 0, "Samples should not be empty");

        for (var i = 0; i < perfview.Samples.length; i++) {
            var s = perfview.Samples[i];
            assert(Array.isArray(s.Stack), "Samples[" + i + "].Stack should be an array");
            assert(s.Stack.length > 0, "Samples[" + i + "].Stack should not be empty");
            assert(typeof s.Metric === "number", "Samples[" + i + "].Metric should be a number");
            assert(s.Metric >= 0, "Samples[" + i + "].Metric should be >= 0, got " + s.Metric);

            for (var j = 0; j < s.Stack.length; j++) {
                assert(typeof s.Stack[j] === "string", "Stack entry should be a string");
            }
        }

        // Sorting: metrics should be descending
        for (var i = 1; i < perfview.Samples.length; i++) {
            assert(
                perfview.Samples[i - 1].Metric >= perfview.Samples[i].Metric,
                "Samples should be sorted descending by Metric"
            );
        }

        console.log("  PerfView validation complete.");

        await browser.close();

        // -----------------------------------------------------------------
        // Summary
        // -----------------------------------------------------------------
        console.log("");
        if (failures > 0) {
            console.error("FAILED: " + failures + " validation(s) failed.");
            process.exit(1);
        } else {
            console.log("ALL VALIDATIONS PASSED.");
            if (hasRealData) {
                console.log("  (validated with live Emscripten profiler data)");
            } else {
                console.log("  (validated with injected allocation data)");
            }
            process.exit(0);
        }
    };

    _run().catch(function (err) {
        console.error("ERROR: " + err);
        process.exit(1);
    });
})();
