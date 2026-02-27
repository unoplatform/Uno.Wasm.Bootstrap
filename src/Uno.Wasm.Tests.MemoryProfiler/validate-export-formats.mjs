/**
 * Validates that the memory profiler export produces correct speedscope and
 * PerfView JSON from mock allocation data.  Runs under Node.js with browser
 * global mocks so the compiled uno-bootstrap.js (namespace-style, module:none)
 * can execute without a real DOM.
 *
 * Usage:  node validate-export-formats.mjs <path-to-uno-bootstrap.js> [output-dir]
 */

import { readFileSync, writeFileSync, mkdirSync, existsSync } from "fs";
import path from "path";

// ---------------------------------------------------------------------------
// CLI args
// ---------------------------------------------------------------------------
const bootstrapJsPath = process.argv[2];
const outputDir = process.argv[3] || "./artifacts";

if (!bootstrapJsPath) {
    console.error("Usage: node validate-export-formats.mjs <path-to-uno-bootstrap.js> [output-dir]");
    process.exit(1);
}

if (!existsSync(bootstrapJsPath)) {
    console.error(`ERROR: File not found: ${bootstrapJsPath}`);
    process.exit(1);
}

mkdirSync(outputDir, { recursive: true });

// ---------------------------------------------------------------------------
// Track validation failures
// ---------------------------------------------------------------------------
let failures = 0;
function assert(condition, message) {
    if (!condition) {
        console.error(`  FAIL: ${message}`);
        failures++;
    }
}

// ---------------------------------------------------------------------------
// 1. Browser mocks
// ---------------------------------------------------------------------------

// Blob: capture the JSON string passed to the constructor
let lastBlobContent = null;
globalThis.Blob = class Blob {
    constructor(parts, _options) {
        // parts is typically [ jsonString ]
        lastBlobContent = parts.join("");
    }
};

// Mock anchor element used by downloadSnapshot
const mockAnchor = {
    href: "",
    download: "",
    click() {},
};

const mockDocument = {
    addEventListener(_event, _handler) {},
    createElement(_tag) {
        return { ...mockAnchor };
    },
    body: {
        appendChild(_el) {},
        removeChild(_el) {},
    },
};

globalThis.document = mockDocument;

globalThis.window = {
    document: mockDocument,
    URL: {
        createObjectURL(_blob) {
            return "blob:mock-url";
        },
        revokeObjectURL(_url) {},
    },
};

// Make sure 'window' properties are accessible directly (some compiled code
// references `window` via globalThis.window but also checks typeof window)
// globalThis already has 'window' set above.

// Prevent the Bootstrapper.bootstrap() auto-invocation from blowing up
// by providing stubs for things it touches.
globalThis.navigator = { platform: "Linux", userAgent: "Node" };

// ---------------------------------------------------------------------------
// 2. Mock profiler data
// ---------------------------------------------------------------------------
globalThis.emscriptenMemoryProfiler = {
    totalMemoryAllocated: 1048576,
    totalMemoryUsedByHeap: 524288,
    totalTimesMallocCalled: 100,
    totalTimesFreeCalled: 50,
    totalTimesReallocCalled: 10,
    stackBase: 0x100000,
    stackTop: 0x200000,
    stackMax: 0x300000,
    stackTopWatermark: 0x250000,
    sbrkValue: 0x400000,
    totalStaticMemory: 65536,
    freeMemory: 262144,
    pagePreRunIsFinished: true,
    updateUi() {},

    // Three mock allocation sites with V8-format stack traces
    allocationsAtLoc: {
        // Site 1: 65536 bytes outstanding (largest)
        [
            "at dotnet.native.wasm.dlmalloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[100]:0x1000)\n" +
            "at dotnet.native.wasm.internal_memalign (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[101]:0x2000)\n" +
            "at dotnet.native.wasm.sgen_card_table_init (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[200]:0x3000)"
        ]: [5, 65536, ""],

        // Site 2: 32768 bytes outstanding
        [
            "at dotnet.native.wasm.dlmalloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[100]:0x1000)\n" +
            "at dotnet.native.wasm.monoeg_malloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[102]:0x4000)\n" +
            "at dotnet.native.wasm.mono_gc_alloc_obj (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[300]:0x5000)"
        ]: [3, 32768, ""],

        // Site 3: 16384 bytes outstanding (smallest)
        [
            "at dotnet.native.wasm.dlmalloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[100]:0x1000)\n" +
            "at dotnet.native.wasm.custom_alloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[400]:0x6000)"
        ]: [1, 16384, ""],

        // Site 4: 0 outstanding (should be skipped)
        [
            "at dotnet.native.wasm.dlmalloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[100]:0x1000)\n" +
            "at dotnet.native.wasm.freed_alloc (http://localhost:8000/_framework/dotnet.native.wasm:wasm-function[500]:0x7000)"
        ]: [0, 0, ""],
    },
};

// ---------------------------------------------------------------------------
// 3. Load the compiled JS
// ---------------------------------------------------------------------------
console.log("Loading compiled uno-bootstrap.js...");

const jsSource = readFileSync(bootstrapJsPath, "utf-8");

// The file ends with `Uno.WebAssembly.Bootstrap.Bootstrapper.bootstrap();`
// which tries to import modules and access the real DOM.  We strip that
// auto-invocation line so we can load the class definitions without side effects.
const safeSource = jsSource.replace(
    /Uno\.WebAssembly\.Bootstrap\.Bootstrapper\.bootstrap\(\);?/g,
    "// (bootstrap auto-invocation stripped for testing)"
);

// Use indirect eval to execute in the true global scope so `var Uno`
// populates globalThis (new Function() would create a local scope).
(0, eval)(safeSource);

const MemProfiler = globalThis.Uno?.WebAssembly?.Bootstrap?.EmscriptenMemoryProfilerSupport;
if (!MemProfiler) {
    console.error("ERROR: Could not find Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport on globalThis.");
    console.error("  The compiled uno-bootstrap.js may be stale. Rebuild with: dotnet build src/Uno.Wasm.Bootstrap/Uno.Wasm.Bootstrap.csproj");
    process.exit(1);
}

if (typeof MemProfiler.downloadSnapshot !== "function") {
    console.error("ERROR: EmscriptenMemoryProfilerSupport.downloadSnapshot is not a function.");
    console.error("  The compiled uno-bootstrap.js is missing the export methods. Rebuild the TypeScript.");
    process.exit(1);
}

// ---------------------------------------------------------------------------
// 4. Validate speedscope format
// ---------------------------------------------------------------------------
console.log("\n=== Validating speedscope format ===");

lastBlobContent = null;
MemProfiler.downloadSnapshot("speedscope");

assert(lastBlobContent !== null, "downloadSnapshot('speedscope') should create a Blob");

let speedscope;
try {
    speedscope = JSON.parse(lastBlobContent);
} catch (e) {
    console.error(`  FAIL: speedscope output is not valid JSON: ${e.message}`);
    process.exit(1);
}

// Write output file
const speedscopeOutPath = path.join(outputDir, "test-output.speedscope.json");
writeFileSync(speedscopeOutPath, JSON.stringify(speedscope, null, 2));
console.log(`  Written: ${speedscopeOutPath}`);

// Schema
assert(
    speedscope.$schema === "https://www.speedscope.app/file-format-schema.json",
    `$schema should be speedscope URL, got: ${speedscope.$schema}`
);

// Shared frames
assert(speedscope.shared && Array.isArray(speedscope.shared.frames), "shared.frames should be an array");
const frames = speedscope.shared.frames;
console.log(`  shared.frames: ${frames.length} entries`);

// Check deduplication: "dlmalloc" appears in all 3 sites but should be a single frame
const dlmallocFrames = frames.filter(f => f.name === "dlmalloc");
assert(dlmallocFrames.length === 1, `dlmalloc should appear exactly once in frames (deduplication), got ${dlmallocFrames.length}`);

// Profile structure
assert(Array.isArray(speedscope.profiles) && speedscope.profiles.length > 0, "profiles should be a non-empty array");
const profile = speedscope.profiles[0];
assert(profile.type === "sampled", `profile.type should be 'sampled', got: ${profile.type}`);
assert(profile.unit === "bytes", `profile.unit should be 'bytes', got: ${profile.unit}`);

// Samples and weights
assert(Array.isArray(profile.samples), "profile.samples should be an array");
assert(Array.isArray(profile.weights), "profile.weights should be an array");
assert(
    profile.samples.length === profile.weights.length,
    `samples.length (${profile.samples.length}) should equal weights.length (${profile.weights.length})`
);

// Should have 3 samples (site 4 has 0 outstanding, should be skipped)
assert(profile.samples.length === 3, `Expected 3 samples (skipping freed sites), got ${profile.samples.length}`);

// Validate frame indices
for (let i = 0; i < profile.samples.length; i++) {
    const sample = profile.samples[i];
    assert(Array.isArray(sample), `samples[${i}] should be an array`);
    for (const idx of sample) {
        assert(typeof idx === "number", `frame index should be a number, got ${typeof idx}`);
        assert(idx >= 0 && idx < frames.length, `frame index ${idx} out of range [0, ${frames.length})`);
    }
}

// Validate weights are positive
for (let i = 0; i < profile.weights.length; i++) {
    assert(typeof profile.weights[i] === "number", `weights[${i}] should be a number`);
    assert(profile.weights[i] > 0, `weights[${i}] should be > 0, got ${profile.weights[i]}`);
}

// endValue should equal sum of weights
const weightSum = profile.weights.reduce((a, b) => a + b, 0);
assert(
    profile.endValue === weightSum,
    `endValue (${profile.endValue}) should equal sum of weights (${weightSum})`
);

// Sorting: first sample should correspond to the largest allocation (65536)
assert(profile.weights[0] === 65536, `First weight should be 65536 (sorted by bytes desc), got ${profile.weights[0]}`);

console.log("  speedscope validation complete.");

// ---------------------------------------------------------------------------
// 5. Validate PerfView format
// ---------------------------------------------------------------------------
console.log("\n=== Validating PerfView format ===");

lastBlobContent = null;
MemProfiler.downloadSnapshot("perfview");

assert(lastBlobContent !== null, "downloadSnapshot('perfview') should create a Blob");

let perfview;
try {
    perfview = JSON.parse(lastBlobContent);
} catch (e) {
    console.error(`  FAIL: PerfView output is not valid JSON: ${e.message}`);
    process.exit(1);
}

// Write output file
const perfviewOutPath = path.join(outputDir, "test-output.PerfView.json");
writeFileSync(perfviewOutPath, JSON.stringify(perfview, null, 2));
console.log(`  Written: ${perfviewOutPath}`);

// Samples array
assert(Array.isArray(perfview.Samples), "Samples should be an array");
assert(perfview.Samples.length === 3, `Expected 3 PerfView samples, got ${perfview.Samples.length}`);

for (let i = 0; i < perfview.Samples.length; i++) {
    const sample = perfview.Samples[i];
    assert(Array.isArray(sample.Stack), `Samples[${i}].Stack should be an array`);
    assert(sample.Stack.length > 0, `Samples[${i}].Stack should not be empty`);
    assert(typeof sample.Metric === "number", `Samples[${i}].Metric should be a number`);
    assert(sample.Metric > 0, `Samples[${i}].Metric should be > 0, got ${sample.Metric}`);

    // Stack entries should be strings (function names)
    for (const entry of sample.Stack) {
        assert(typeof entry === "string", `Stack entry should be a string, got ${typeof entry}`);
    }
}

// Sorting: first sample should be the largest
assert(perfview.Samples[0].Metric === 65536, `First PerfView sample metric should be 65536, got ${perfview.Samples[0].Metric}`);

console.log("  PerfView validation complete.");

// ---------------------------------------------------------------------------
// 6. Summary
// ---------------------------------------------------------------------------
console.log("");
if (failures > 0) {
    console.error(`FAILED: ${failures} validation(s) failed.`);
    process.exit(1);
} else {
    console.log("ALL VALIDATIONS PASSED.");
    process.exit(0);
}
