# Feature Specification: Native Allocation Profiling Export via Hotkey

**Feature Branch**: `dev/jela/fingerprint3`
**Created**: 2026-02-26
**Updated**: 2026-02-26
**Status**: In Progress

## Overview

When Emscripten's `--memoryprofiler` flag is enabled, detailed native allocation tracking data is maintained in `globalThis.emscriptenMemoryProfiler`. This data includes per-callsite allocation counts, outstanding bytes, and stack traces for every `malloc`/`free`/`realloc` call. However, the profiler's built-in HTML UI is destroyed by the application's DOM rendering, and the raw data structures are not easily exportable for offline analysis.

Developers need a way to capture a structured snapshot of the current allocation state and download it as a JSON file for analysis by humans or automated tools (e.g., an AI agent performing leak detection). The export must parse raw Emscripten stack traces into structured frames, identify the true caller by skipping allocator wrapper functions, and produce a machine-readable document.

The feature is triggered entirely on the JavaScript side via a `Ctrl+Shift+H` hotkey, avoiding managed-side memory overhead.

## User Scenarios & Testing

### P1: Developer Exports Snapshot for Analysis

**User Journey**: A developer suspects a memory leak in their Uno WebAssembly application. They enable the memory profiler via `WasmShellExtraEmccFlags` with `--memoryprofiler`, load the app, exercise the leaky scenario, then press `Ctrl+Shift+H` to download a JSON snapshot. They open the file and inspect which allocation sites have the most outstanding bytes.

**Priority Justification**: This is the primary use case. Developers need actionable data about where native memory is allocated and whether it is being freed.

**Independent Test Approach**: Build and serve the RayTracer sample with `--memoryprofiler` enabled. After the app loads, press `Ctrl+Shift+H` and verify a JSON file is downloaded.

**Acceptance Scenarios**:

```gherkin
Given a Uno WebAssembly application with --memoryprofiler enabled
When the developer presses Ctrl+Shift+H after the application has loaded
Then a JSON file named "memory-profile-{timestamp}.speedscope.json" should be downloaded
And the JSON should conform to the speedscope file format schema
And the JSON should contain a "shared.frames" array of deduplicated function names
And the JSON should contain a "profiles" array with one sampled profile
And each sample should be an array of frame indices in root-to-leaf order
And each weight should be the outstanding bytes for that allocation site
```

**Edge Cases**:

- Application with no active allocations (all freed) — `allocationSites` should be empty
- Application with hundreds of allocation sites — file may be several hundred KB
- Profiler not available (flag not passed) — hotkey should be silently inactive

### P2: Agent Consumes the JSON for Automated Leak Detection

**User Journey**: An AI agent or automated tool receives the exported JSON file. It parses the structured allocation sites, sorts by `outstandingBytes` descending, and identifies the top allocation sites that are likely leaks.

**Priority Justification**: Machine-readable output enables automated analysis workflows, reducing the manual effort needed to diagnose leaks.

**Independent Test Approach**: Export a snapshot from the RayTracer sample, then write a script that parses the JSON, verifies the schema, and lists the top 10 allocation sites by bytes.

**Acceptance Scenarios**:

```gherkin
Given an exported memory profile in speedscope format
When an automated tool parses the file
Then it should conform to the speedscope file format schema
And the "profiles[0].weights" array should be sorted descending (largest allocations first)
And the frame names should identify meaningful function names (not just allocator wrappers)
```

### P3+: Comparing Before/After Scenarios

**User Journey**: A developer captures two snapshots (e.g., before and after a specific operation) by pressing `Ctrl+Shift+H` at different moments. They compare the two JSON files to identify allocation sites that grew.

**Priority Justification**: Differential analysis is the most powerful leak detection technique, but it can be done offline with the exported files.

**Independent Test Approach**: Capture two snapshots at different points during RayTracer execution. Compare the files and verify that allocation counts have changed between snapshots.

**Acceptance Scenarios**:

```gherkin
Given two exported memory profile JSON files from different moments
When a developer or tool compares the "allocationSites" arrays
Then sites present in both files can be matched by "callSiteKey"
And the difference in "outstandingBytes" reveals memory growth or shrinkage
```

## Requirements

### Functional Requirements

**FR-1**: When the memory profiler is active, pressing `Ctrl+Shift+H` SHALL trigger an immediate download of a JSON file in speedscope format containing the current allocation data.

**FR-2**: The hotkey-triggered download SHALL produce a file named `memory-profile-{timestamp}.speedscope.json` where `{timestamp}` is the ISO 8601 date-time string at the moment of capture.

**FR-3**: The `downloadSnapshot()` method SHALL accept an optional format parameter (`"speedscope"` or `"perfview"`), defaulting to `"speedscope"`.

**FR-4**: The speedscope export SHALL conform to the [speedscope file format schema](https://www.speedscope.app/file-format-schema.json) with:
- `shared.frames`: deduplicated array of function names
- `profiles[0].type`: `"sampled"`
- `profiles[0].unit`: `"bytes"`
- `profiles[0].samples`: arrays of frame indices in root-to-leaf (outermost-first) order
- `profiles[0].weights`: outstanding bytes per allocation site

**FR-5**: The PerfView export SHALL produce a JSON file named `memory-profile-{timestamp}.PerfView.json` with:
- `Samples`: array of objects, each with `Stack` (function names innermost-first) and `Metric` (outstanding bytes)

**FR-6**: Allocation sites SHALL be sorted by `outstandingBytes` descending before export in both formats.

**FR-7**: The `callSiteKey` SHALL be derived by skipping known allocator wrapper functions (`dlmalloc`, `internal_memalign`, `dlcalloc`, `dlposix_memalign`, `monoeg_malloc`, `monoeg_g_calloc`, `monoeg_malloc0`) and using the first non-wrapper function name.

**FR-8**: The `getSnapshotJson()` method SHALL continue to provide summary counters for programmatic access from managed code.

**FR-9**: If the profiler is not available (flag not passed to emcc), the hotkey SHALL not be registered and no error shall occur.

### Key Entities

**Allocation Site**: A unique call location identified by its raw stack trace. Tracked by Emscripten in `allocationsAtLoc` keyed by the `Error` stack string.

**Stack Frame**: A single frame in a parsed stack trace containing the function name, WASM function index, and byte offset.

**Call Site Key**: A normalized identifier for an allocation site, derived by finding the first non-allocator function in the stack trace.

**Heap Growth Event**: A record of a `sbrk` call that grew the WASM heap, with begin/end addresses and the triggering stack trace.

## Export Formats

Two industry-standard export formats are supported, selected via the `downloadSnapshot()` method's format parameter. The hotkey (`Ctrl+Shift+H`) defaults to speedscope.

### Speedscope Format (default)

File extension: `.speedscope.json`. Open at [speedscope.app](https://www.speedscope.app) for interactive flame graph visualization.

```json
{
  "$schema": "https://www.speedscope.app/file-format-schema.json",
  "shared": {
    "frames": [
      { "name": "sgen_card_table_init" },
      { "name": "internal_memalign" },
      { "name": "dlmalloc" }
    ]
  },
  "profiles": [{
    "type": "sampled",
    "name": "Native Memory Allocations",
    "unit": "bytes",
    "startValue": 0,
    "endValue": 8404992,
    "samples": [[0, 1, 2]],
    "weights": [8404992]
  }]
}
```

- `shared.frames` — deduplicated list of all function names across all allocation sites
- `samples[i]` — array of frame indices for allocation site `i`, in root-to-leaf order (outermost first, as speedscope expects)
- `weights[i]` — outstanding bytes for that allocation site
- `unit` = `"bytes"` so speedscope labels the axis correctly

### PerfView Format

File extension: `.PerfView.json`. Open in [PerfView](https://github.com/microsoft/perfview) for stack analysis.

```json
{
  "Samples": [
    {
      "Stack": ["dlmalloc", "internal_memalign", "sgen_card_table_init"],
      "Metric": 8404992
    }
  ]
}
```

- `Stack` — array of function names, innermost first (matches our existing frame order)
- `Metric` — outstanding bytes for that allocation site
```

## Implementation

### Modified Files

**`src/Uno.Wasm.Bootstrap/ts/Uno/WebAssembly/EmscriptenMemoryProfilerSupport.ts`**

The existing class is extended with:

1. **Bug fixes in `getSnapshotJson()`**:
   - `allocationSiteCount` now reads `Object.keys(profiler.allocationsAtLoc || {}).length` instead of a nonexistent property
   - `pagePreRunIsFinished` now reads the correct property name instead of `pageprealiased`

2. **Stack trace parser** (`parseStackTrace`): Regex-based parser for Emscripten/V8 stack trace format. Extracts function name, WASM function index, and byte offset from each frame. Strips the `dotnet.native.wasm.` prefix for readability.

3. **Allocator wrapper detection** (`ALLOCATOR_FUNCTIONS`, `deriveCallSiteKey`): Set of known allocator function names to skip when determining the true call site. Returns the first non-allocator function name.

4. **Format-specific builders** (`buildSpeedscopeDocument`, `buildPerfViewDocument`): Build industry-standard JSON from captured allocation sites. Speedscope format deduplicates frames into a shared table and reverses stack order to root→leaf. PerfView format uses innermost-first stack order.

5. **Allocation capture** (`captureAllocationSites`, `captureSbrkSources`): Iterates `allocationsAtLoc` and `sbrkSources`, parses stack traces, and produces structured snapshots.

6. **Hotkey handler** (`attachHotKey`): Registers `Ctrl+Shift+H` keydown listener during `initialize()`. Defaults to speedscope format. Follows the existing pattern from `AotProfilerSupport` and `LogProfilerSupport`.

7. **File download** (`downloadSnapshot`): Public method accepting a format parameter (`"speedscope"` or `"perfview"`). Creates a Blob from the JSON, generates a download link, and triggers the download. Same anchor-click pattern used by `AotProfilerSupport.saveAotProfile()`.

### How to Enable

Set the `WasmShellEnableWasmMemoryProfiler` property in your project file:

```xml
<PropertyGroup>
  <WasmShellEnableWasmMemoryProfiler>true</WasmShellEnableWasmMemoryProfiler>
</PropertyGroup>
```

This adds the `--memoryprofiler` flag to the Emscripten compiler and sets `config.enable_memory_profiler = true` in the generated configuration, which activates the `EmscriptenMemoryProfilerSupport.initialize()` path and registers the hotkey.

## Success Criteria

**SC-1**: Pressing `Ctrl+Shift+H` in a `--memoryprofiler`-enabled application SHALL download a valid `.speedscope.json` file.

**SC-2**: The speedscope JSON SHALL open in speedscope.app and render a flame graph with byte-weighted allocation sites.

**SC-3**: Running `downloadSnapshot("perfview")` from the browser console SHALL download a valid `.PerfView.json` file.

**SC-4**: The PerfView JSON SHALL be openable in PerfView and display allocation site stacks with metrics.

**SC-5**: Both formats SHALL be parseable by `JSON.parse()` and conform to their respective documented schemas.

**SC-6**: When the profiler is not enabled, pressing `Ctrl+Shift+H` SHALL have no effect and produce no console errors.

## Out of Scope

- Multi-snapshot session export from a single page load
- Real-time streaming of allocation data
- Support for non-V8 stack trace formats (Firefox, Safari)
- Integration with external profiling tools (Chrome DevTools, perf)
- Compression of the exported JSON
- Customizable hotkey binding
