---
uid: Uno.Wasm.Bootstrap.Profiling.NativeMemory
---

# Native Memory Profiling

Emscripten provides a built-in memory profiler that tracks every native `malloc`, `free`, and `realloc` call. When enabled, it records per-callsite allocation counts, outstanding bytes, and full stack traces. The Uno.Wasm.Bootstrap integrates this profiler and provides a hotkey to export the data as structured JSON for offline analysis.

This is complementary to the [managed memory profiling](features-profiling.md) which tracks .NET GC allocations. Native memory profiling covers the WebAssembly linear memory layer below the GC, including runtime internals, interop buffers, and Emscripten's own allocations.

## Enabling the Native Memory Profiler

Set the `WasmShellEnableWasmMemoryProfiler` property in your project file:

```xml
<PropertyGroup>
    <WasmShellEnableWasmMemoryProfiler>true</WasmShellEnableWasmMemoryProfiler>
</PropertyGroup>
```

Then rebuild your application. On startup, the console will display:

```
[MemoryProfiler] Emscripten memory profiler bridge activated.
[MemoryProfiler] Export hotkey: Ctrl+Shift+H (speedscope format).
  For PerfView format, run: Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.downloadSnapshot("perfview")
```

> [!NOTE]
> The profiler adds overhead to every allocation. Use it during development and investigation only, not in production builds.

## Exporting a Snapshot

Once the application is running, press **Ctrl+Shift+H** while the application has focus. This immediately downloads a file in [speedscope](https://www.speedscope.app) format (`.speedscope.json`) containing the current allocation data.

For PerfView format, run the following in the browser console:

```javascript
Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.downloadSnapshot("perfview")
```

You can press the hotkey multiple times at different points during execution to capture snapshots for comparison.

## Export Formats

Two industry-standard formats are supported:

| Format | Extension | Viewer | When to use |
|--------|-----------|--------|-------------|
| Speedscope (default) | `.speedscope.json` | [speedscope.app](https://www.speedscope.app) | Cross-platform, interactive flame graphs in any browser |
| PerfView | `.PerfView.json` | [PerfView](https://github.com/microsoft/perfview) | Windows, powerful stack grouping and diffing |

### Speedscope Format (default)

The speedscope format uses a shared frame table with index-based references for compact representation:

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

- `shared.frames` — deduplicated list of all function names
- `samples[i]` — frame indices for allocation site `i`, root-to-leaf order
- `weights[i]` — outstanding bytes for that allocation site
- `unit` = `"bytes"` — speedscope labels the axis as bytes

**To view**: Open [speedscope.app](https://www.speedscope.app) and drag the `.speedscope.json` file onto the page.

### PerfView Format

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

- `Stack` — function names innermost-first
- `Metric` — outstanding bytes

**To view**: Open PerfView, use **File > Open** and select the `.PerfView.json` file.

## Analyzing the Data

### Finding the Largest Allocations

In speedscope, switch to the **Left Heavy** view to see which call stacks consume the most bytes. The widest bars represent the largest memory consumers.

In PerfView, use the **By Name** view to see total bytes attributed to each function, or **CallTree** for full stack analysis.

### Detecting Leaks by Comparison

1. Load your application and let it reach a steady state
2. Press **Ctrl+Shift+H** to export a baseline snapshot
3. Perform the operation you suspect leaks memory
4. Press **Ctrl+Shift+H** again to export a second snapshot
5. In speedscope: open both files and visually compare the flame graphs
6. In PerfView: use the **Diff** feature to compare two `.PerfView.json` files and find allocation sites that grew

## Programmatic Access

The export methods are available from the browser console:

```javascript
// Download speedscope format (default)
Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.downloadSnapshot()

// Download PerfView format
Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.downloadSnapshot("perfview")

// Get summary counters as JSON
Uno.WebAssembly.Bootstrap.EmscriptenMemoryProfilerSupport.getSnapshotJson()
```

## Relationship to Other Tools

| Tool | What it covers | How to enable |
|------|---------------|---------------|
| [Managed memory profiler](features-profiling.md) | .NET GC allocations (managed objects) | `WasmShellEnableLogProfiler` |
| **Native memory profiler** (this page) | `malloc`/`free` in WebAssembly linear memory | `WasmShellEnableWasmMemoryProfiler` property |
| [Address Sanitizer](features-memory-corruption-troubleshooting.md) | Memory corruption, use-after-free, buffer overflows | `-fsanitize=address` in `WasmShellExtraEmccFlags` |
| Browser DevTools Memory tab | JavaScript heap, DOM nodes | Built into browser |
