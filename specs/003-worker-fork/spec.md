# Feature Specification: Fork-to-WebWorker API

**Feature Branch**: `dev/jela/fork`
**Created**: 2026-03-09
**Updated**: 2026-03-09
**Status**: Draft

## Overview

When running a .NET WebAssembly application bootstrapped by Uno.Wasm.Bootstrap, the browser has already compiled the application's WebAssembly module (typically several megabytes of machine code). Spawning a second instance of the application currently requires loading and compiling the WASM binary from scratch in a new context, which is both slow and memory-intensive.

Developers need a way to "fork" the currently running application into a Web Worker that **reuses the already-compiled `WebAssembly.Module`** from the main thread. The worker creates a new `WebAssembly.Instance` with fresh linear memory and bootstraps a separate .NET runtime, but the underlying compiled machine code is shared by the browser. This enables scenarios such as background computation, parallel processing pipelines, and message-driven worker architectures — all without the cost of recompiling the WASM binary.

The feature exposes a JavaScript API (`Uno.WebAssembly.Bootstrap.WorkerFork.forkToWorker(options)`) that creates a module Web Worker from an inline blob, posts the compiled module via `postMessage` (which the browser clones cheaply via the compiled-code cache), and invokes the C# `Main` entry point with caller-specified arguments. A bidirectional message channel allows the main thread and the worker to exchange JSON payloads via `[JSImport]`/`[JSExport]` interop.

## User Scenarios & Testing

### P1: Fork an Application to a Background Worker

**User Journey**: A developer has a .NET WebAssembly application that needs to perform CPU-intensive work without blocking the UI. They call the `forkToWorker` API from their `Main` method, passing `["--worker"]` as arguments. The forked worker detects the argument, enters a message-processing loop, and echoes back results. The main thread receives the results and updates the DOM.

**Priority Justification**: This is the primary use case — offloading work to a background thread while sharing the compiled WASM module to avoid recompilation.

**Independent Test Approach**: Build and serve the `Uno.Wasm.Tests.WorkerFork.App` sample. Open in browser, verify that a `#results` div appears with the worker's response.

**Acceptance Scenarios**:

```gherkin
Given a Uno WebAssembly application that has finished loading
When the application calls WorkerFork.forkToWorker({ args: ['--worker'] })
Then a Web Worker should be created without recompiling the WASM binary
And the worker should bootstrap a fresh .NET runtime using the shared compiled module
And the worker should invoke Main with the provided arguments
And the worker.ready promise should resolve when the runtime is initialized
And messages sent via handle.postMessage should be receivable by the worker
And messages sent by the worker via __unoWorkerPostMessage should be receivable on the main thread
```

**Edge Cases**:

- Worker creation before .NET runtime is fully initialized — should throw a clear error
- Multiple concurrent forks — each should get independent .NET runtime instances
- Worker termination via `handle.terminate()` — should cleanly kill the worker
- Large message payloads (> 1MB) — should transfer without issues

### P2: Worker Detects It Is Running in a Forked Context

**User Journey**: The same C# `Main` entry point runs in both the main thread and the forked worker. The developer uses the `UNO_BOOTSTRAP_IS_WORKER` environment variable or command-line arguments to branch behavior.

**Priority Justification**: The dual-mode `Main` pattern is essential for the fork model where the same assembly runs in both contexts.

**Independent Test Approach**: In the test sample, verify that the worker branch executes by checking console output and the echoed message content.

**Acceptance Scenarios**:

```gherkin
Given a forked worker started with args ['--worker']
When the C# Main entry point executes in the worker
Then Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_IS_WORKER") should return "true"
And the args parameter should contain "--worker"
And the worker should be able to use [JSImport] to call globalThis.__unoWorkerPostMessage
```

### P3+: Memory Savings from Shared Compiled Module

**User Journey**: A developer forks multiple workers and observes that memory usage is lower than if each worker had compiled the WASM binary independently.

**Priority Justification**: Memory efficiency is a key benefit but is browser-implementation-dependent and harder to test deterministically.

**Independent Test Approach**: Profile memory in Chrome DevTools with 3 forked workers vs. 3 independently loaded instances. Compare `WebAssembly.compiledModule` memory entries.

**Acceptance Scenarios**:

```gherkin
Given a Uno WebAssembly application with a 10MB WASM binary
When 3 workers are forked using forkToWorker
Then the total compiled WASM code memory should be approximately 10MB (shared)
  rather than 40MB (4 independent compilations)
And each worker should have its own independent linear memory
```

## Requirements

### Functional Requirements

**FR-1**: The `WorkerFork.forkToWorker(options?)` method SHALL create a new Web Worker that reuses the `WebAssembly.Module` from `globalThis.Module.wasmModule`.

**FR-2**: The compiled `WebAssembly.Module` SHALL be passed to the worker via `postMessage`, leveraging the browser's structured clone algorithm which efficiently shares compiled code.

**FR-3**: The worker SHALL bootstrap a fresh .NET runtime by dynamically importing `_framework/{dotnet_js_filename}` using an absolute URL resolved from the main thread's base URI.

**FR-4**: The worker's runtime SHALL be configured with `instantiateWasm` to call `WebAssembly.instantiate(sharedModule, imports)` instead of fetching and compiling the WASM binary.

**FR-5**: The worker SHALL set the environment variable `UNO_BOOTSTRAP_IS_WORKER=true` before invoking `Main`.

**FR-6**: The worker SHALL invoke `dotnetRuntime.runMain(mainAssemblyName, args)` where `args` are provided by the caller via `options.args`.

**FR-7**: The returned `WorkerHandle` SHALL provide:
- `postMessage(data)` — send a JSON-serializable message to the worker
- `terminate()` — immediately terminate the worker
- `ready: Promise<void>` — resolves when the worker's .NET runtime is initialized

**FR-8**: The worker SHALL expose `self.__unoWorkerPostMessage(jsonString)` for C# code to send messages back to the main thread via `[JSImport]`.

**FR-9**: The worker SHALL expose `self.__unoWorkerMessageCallback` as a settable function that C# code can register via `[JSExport]` to receive messages from the main thread.

**FR-10**: If `globalThis.Module.wasmModule` is not available when `forkToWorker` is called, the method SHALL throw an `Error` with a descriptive message.

**FR-11**: The API SHALL support calling `forkToWorker` multiple times to create multiple independent workers.

**FR-12**: The `options.onMessage` callback SHALL be invoked with the deserialized payload when the worker sends an application-level message.

### Key Entities

**WorkerForkOptions**: Configuration object with optional `args` (string array), `onMessage` (callback), and `onError` (callback).

**WorkerHandle**: Returned handle providing `postMessage`, `terminate`, and `ready` members for interacting with the forked worker.

**Compiled WebAssembly.Module**: The browser's internal representation of compiled WASM machine code, obtainable from `Module.wasmModule` after Emscripten initialization. This object is structured-cloneable (cheaply shared via `postMessage`).

**Worker Message Protocol**: Typed messages exchanged between the main thread and worker, distinguished by a `type` field prefixed with `uno:worker:`.

## Message Protocol

### Main Thread → Worker

| Type | Payload | When |
|------|---------|------|
| `uno:worker:init` | `{ wasmModule, unoConfig, args, envVars, dotnetJsUrl, appBaseUrl }` | Immediately after worker creation |
| `uno:worker:message` | `{ payload: <any> }` | Application-level message via `handle.postMessage()` |

### Worker → Main Thread

| Type | Payload | When |
|------|---------|------|
| `uno:worker:ready` | `{}` | Runtime initialized, `Main` is running |
| `uno:worker:error` | `{ error: string }` | Fatal initialization error |
| `uno:worker:message` | `{ payload: <any> }` | Application-level message via `__unoWorkerPostMessage()` |

## Implementation

### New Files

**`src/Uno.Wasm.Bootstrap/ts/Uno/WebAssembly/WorkerFork.ts`**

New TypeScript file in the `Uno.WebAssembly.Bootstrap` namespace. Contains:

1. **Interfaces**: `WorkerForkOptions`, `WorkerHandle`
2. **`WorkerFork` class** with static `forkToWorker(options?)` method
3. **`buildWorkerScript()` private method** that generates the inline worker blob script as a string

The worker blob script:
- Listens for the `uno:worker:init` message
- Dynamically imports the .NET runtime JS via `import(dotnetJsUrl)`
- Configures `instantiateWasm` to reuse the received `WebAssembly.Module`
- Copies environment variables from the main thread's `UnoConfig` and adds `UNO_BOOTSTRAP_IS_WORKER=true`
- Calls `dotnetRuntime.runMain(mainAssembly, args)`
- Exposes `self.__unoWorkerPostMessage(json)` and `self.__unoWorkerMessageCallback` for C#↔JS interop
- Posts `uno:worker:ready` on success or `uno:worker:error` on failure

**`src/Uno.Wasm.Tests.WorkerFork.App/Uno.Wasm.Tests.WorkerFork.App.csproj`**

Test project following the `Uno.Wasm.Tests.VfsAssemblyLoad.App` pattern:
- SDK: `Microsoft.NET.Sdk.WebAssembly`
- TargetFramework: `net10.0`
- OutputType: `Exe`
- Imports `Uno.Wasm.Tests.Shared.projitems` for the `WebAssembly.Runtime.InvokeJS` helper
- References the Bootstrap project (non-assembly ref)

**`src/Uno.Wasm.Tests.WorkerFork.App/Program.cs`**

Dual-mode entry point:
- Detects worker via `args` containing `"--worker"` or env var `UNO_BOOTSTRAP_IS_WORKER == "true"`
- **Main mode**: Calls `WorkerFork.forkToWorker` via `InvokeJS`, sends a test message when ready, writes worker response to a `#results` DOM element
- **Worker mode**: Registers `__unoWorkerMessageCallback` to call a `[JSExport]` method, echoes received messages back with modification via `__unoWorkerPostMessage`

### Modified Files

**`src/Uno.Wasm.Bootstrap/ts/Uno/WebAssembly/Bootstrapper.ts`**

- Add `/// <reference path="WorkerFork.ts"/>` at the top with the other reference directives
- Add a static convenience method:
  ```typescript
  public static forkToWorker(options?: WorkerForkOptions): WorkerHandle {
      return WorkerFork.forkToWorker(options);
  }
  ```
- The `WorkerFork` class is automatically accessible at `globalThis.Uno.WebAssembly.Bootstrap.WorkerFork` via the existing `globalThis.Uno = Uno` assignment (line 80)

**`src/Uno.Wasm.Bootstrap.sln`**

Add the new test project.

### No Changes Required

- **`tsconfig.json`**: Already includes `ts/**/*`, new `.ts` files are auto-discovered
- **`Uno.Wasm.Bootstrap.csproj`**: TypeScript compilation pipeline already handles all files
- **`ShellTask.cs`**: Worker receives config via `postMessage`, not via `uno-config.js`

## Browser Compatibility

The feature uses module Web Workers (`new Worker(blobUrl, { type: 'module' })`), which requires:
- Chrome 80+
- Edge 80+
- Firefox 114+
- Safari 15+

The `WebAssembly.Module` structured clone is supported in all browsers that support WebAssembly.

## Success Criteria

**SC-1**: Calling `forkToWorker({ args: ['--worker'] })` SHALL create a worker that runs the C# `Main` with `args[0] == "--worker"` without recompiling the WASM binary.

**SC-2**: The `ready` promise SHALL resolve within a reasonable time (< 10 seconds on a local server) indicating the worker's .NET runtime has initialized.

**SC-3**: Messages sent via `handle.postMessage()` SHALL be receivable by C# code in the worker through the `__unoWorkerMessageCallback` interop.

**SC-4**: Messages sent via `__unoWorkerPostMessage()` from the worker SHALL trigger the `onMessage` callback on the main thread.

**SC-5**: The test sample SHALL display a `#results` div containing the worker's echoed response, confirming end-to-end communication.

**SC-6**: Calling `forkToWorker` before the runtime is initialized SHALL throw a descriptive `Error`.

**SC-7**: Multiple calls to `forkToWorker` SHALL create independent workers with separate .NET runtime instances.

## Out of Scope

- SharedArrayBuffer-based shared memory between main thread and workers
- Automatic load balancing or worker pool management
- Transferable object support beyond standard structured clone
- Graceful shutdown protocol (workers are terminated immediately)
- Node.js worker_threads support (browser-only)
- Service Worker integration
- Streaming compilation sharing (only pre-compiled module sharing)
