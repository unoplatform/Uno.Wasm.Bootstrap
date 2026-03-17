# Worker Fork Feature - Progress

## Status: End-to-end working with WebAssembly.Module reuse

## What Works

- `WorkerFork.ts` created with `forkToWorker()` API
- Worker blob script with console relay, message passing, error handling
- `Bootstrapper.ts` modified with `installWasmModuleCapture()` and convenience method
- `globalThis.__unoWasmModule` capture via `WebAssembly.compile/compileStreaming` wrappers
- **WebAssembly.Module reuse**: Worker intercepts `compileStreaming` to reuse the main thread's pre-compiled module
- **Download skip**: Worker uses `withResourceLoader` to skip downloading `dotnet.native.wasm` (~8 MB saved)
- Test project `Uno.Wasm.Tests.WorkerFork.App` with dual-mode `Main()`
- CI pipeline step added to `stage-build-linux-tests.yml`
- Solution file updated
- Puppeteer validation script created
- **Full end-to-end flow verified**: runtime init → Main → message exchange → #results div

## Issues Resolved

1. **`Module.wasmModule` undefined** - Fixed by wrapping global `WebAssembly.compile*` APIs
2. **`WebAssembly.instantiateStreaming is not a function`** - Fixed by using `(<any>globalThis).WebAssembly`
3. **`CompileError: expected magic word`** - Fixed by abandoning custom `instantiateWasm`/`locateFile`
4. **`ManagedError: null`** - Fixed by wrapping `RunMain()` in try-catch
5. **Worker `import()` failure** - Fixed by resolving `_framework/` relative to `document.baseURI`
6. **Worker console not visible** - Fixed by adding console relay (`uno:worker:log` messages)
7. **`Failed to load config file ./mono-config.json`** - Fixed by removing `configSrc`
8. **Emscripten `ENVIRONMENT_IS_WORKER` false in module worker** - Fixed by using classic worker
9. **`create()` hangs forever in worker** - ROOT CAUSE: .NET runtime checks `globalThis.onmessage`
   to detect worker context. If set, it skips asset promise resolution (thinks it's a pthread deputy).
   Fixed by using `addEventListener("message", ...)` instead of `self.onmessage = ...`.
   See: https://github.com/dotnet/runtime/issues/114918
10. **`Uno not found` when calling InvokeJS** - Fixed by providing `globalThis.Uno.WebAssembly.Bootstrap.Bootstrapper.invokeJS` shim in worker
11. **170+ assembly downloads in worker** - Fixed by clean rebuild with `PublishTrimmed=true` (stale build artifacts)
12. **`withModuleConfig({instantiateWasm})` breaks JS eval path** - Using `withModuleConfig` to provide
    an `instantiateWasm` callback breaks the .NET runtime's `[JSImport]` eval mechanism. The eval'd JS
    code from C# `InvokeJS` produces no console output and interop globals become invisible.
    ROOT CAUSE: Not fully diagnosed, but the Emscripten module config change appears to alter how
    the runtime resolves JS globals. Fixed by using global `WebAssembly.compileStreaming` interception
    instead of `withModuleConfig`.

## Memory Measurements

### Before/After Worker Fork (with Module Reuse Enabled)

| Metric | Before | After | Delta |
|--------|--------|-------|-------|
| Renderer RSS | 141 MB | 164 MB | **+23 MB** |
| Renderer PSS | 91 MB | 114 MB | **+23 MB** |
| Renderer Private | 65 MB | 88 MB | **+23 MB** |
| Main JS Heap | 2.03 MB | 2.04 MB | +0.01 MB |
| WASM linear memory (main) | 32.0 MB | — | — |

### Analysis

- **Worker fork cost: ~23 MB** of additional private memory (irreducible)
- Memory delta is the same with or without module reuse (~23 MB either way)
- V8/Chromium already shares compiled WASM code pages internally between instances of the same module
- The 23 MB is the irreducible cost of a second .NET runtime instance:
  - WASM linear memory (~32 MB allocated, ~8-12 MB committed/demand-paged)
  - .NET managed heap and runtime state
  - Assembly metadata
- **Module reuse benefits are primarily startup performance, not memory**:
  1. Skips ~8 MB `dotnet.native.wasm` download (network bandwidth savings)
  2. Skips WASM compilation time (CPU savings, faster worker startup)
  3. V8 code cache sharing happens automatically regardless

### Key Insight

`WebAssembly.Module` sharing via `postMessage` doesn't reduce RSS/PSS because:

1. Chromium workers run in the same renderer process
2. V8 already deduplicates compiled code for identical modules
3. Each instance still needs its own linear memory and runtime state

## Next Steps

- [ ] Clean up stale diagnostic files (diagnose.js, diagnose2.js, measure-*.js, quick-diag.js)
- [ ] Run CI validation (Puppeteer test in pipeline)
- [ ] Verify VfsAssemblyLoad test still passes (regression check)

## Key Files

- `src/Uno.Wasm.Bootstrap/ts/Uno/WebAssembly/WorkerFork.ts` - Main implementation
- `src/Uno.Wasm.Bootstrap/ts/Uno/WebAssembly/Bootstrapper.ts` - Modified for module capture
- `src/Uno.Wasm.Tests.WorkerFork.App/Program.cs` - Test app
- `src/Uno.Wasm.Tests.WorkerFork/validate-worker-fork.js` - Puppeteer tests
- `src/Uno.Wasm.Tests.WorkerFork/test-worker-fork.sh` - CI test script

## Technical Notes

### .NET Runtime in Workers

- .NET 10 embeds boot config JSON at end of `dotnet.js`
- `dotnet.native.js` uses `typeof importScripts=="function"` for worker detection
- Module workers don't have `importScripts`, so must use classic workers
- `import()` (dynamic import) works in classic workers in modern browsers
- **CRITICAL**: Must NOT set `self.onmessage` before `dotnet.create()` — use `addEventListener` instead
- The `.dotnet.create()` builder API works in workers (when onmessage is not set)
- `uno_main` config field is no longer populated; `runMain(undefined, args)` uses default entry point
- Worker needs `Uno.WebAssembly.Bootstrap.Bootstrapper.invokeJS` shim for C# `InvokeJS` calls
- COEP/COOP headers needed for SharedArrayBuffer support

### Module Reuse Strategy

- Main thread captures `WebAssembly.Module` via `installWasmModuleCapture()` wrappers
- Module is stored on `globalThis.__unoWasmModule` and sent to worker via `postMessage`
- Worker intercepts `WebAssembly.compileStreaming` to return the pre-compiled module
- Worker uses `withResourceLoader` to skip downloading `dotnet.native.wasm`
- **DO NOT use `withModuleConfig({instantiateWasm})`** — it breaks the runtime's JS eval path
- The .NET runtime calls `WebAssembly.compileStreaming(fetch(wasmUrl))` during init

### Message Callback Pattern

- Worker exposes `self.__unoWorkerSetMessageCallback(fn)` for C# to register message handlers
- This function properly drains any messages that arrived before registration
- `Object.defineProperty` setter on `__unoWorkerMessageCallback` also works as fallback
- C# code should prefer `__unoWorkerSetMessageCallback` for reliable pending message delivery
