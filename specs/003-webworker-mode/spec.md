# Feature Specification: WebWorker Shell Mode

**Feature Branch**: `dev/jela/webworker`
**Created**: 2026-03-16
**Updated**: 2026-03-16
**Status**: In Progress

## Overview

The Uno.Wasm.Bootstrap supports three shell modes: `Browser` (default HTML page), `Node` (server-side), and `BrowserEmbedded` (embeddable JS script). This feature adds a fourth mode, `WebWorker`, that generates a self-contained worker bootstrap script. The worker runs the .NET runtime in a Web Worker thread, enabling background execution without blocking the main UI thread.

A WebWorker project is designed to be consumed by a host Browser-mode project. The host sets `WasmShellWebWorkerProject` to point to the worker `.csproj`, and the build system publishes the worker and copies its output (including its own `_framework/`) into a subdirectory of the host's wwwroot.

## User Scenarios & Testing

### P1: Host App Creates Worker and Receives Messages

**User Journey**: A developer creates two projects — a Browser-mode host and a WebWorker-mode worker. The host's `Program.cs` uses `InvokeJS` to create a `Worker` pointing to `_worker/worker.js`. The worker's `Program.cs` runs computations and posts results back via `self.postMessage`. The host receives the messages and updates the UI.

**Priority Justification**: This is the primary use case — offloading computation to a background thread while keeping the UI responsive.

**Independent Test Approach**: Publish the host project (which triggers worker publish), serve the output, and verify via Puppeteer that the worker posts a message received by the host.

**Acceptance Scenarios**:

```gherkin
Given a host Browser-mode project with WasmShellWebWorkerProject set
When the developer runs dotnet publish on the host
Then the host's publish output SHALL contain _worker/worker.js
And _worker/_framework/ SHALL contain the worker's .NET runtime files
And the host's _framework/ SHALL contain its own .NET runtime files (no conflicts)
And the worker.js SHALL correctly reference _worker/_framework/ via relative paths

Given a published host+worker application served in a browser
When the host's Program.cs creates a Worker at ./_worker/worker.js
And the worker's Program.cs calls self.postMessage via InvokeJS
Then the host SHALL receive the message
And the #results element SHALL display the worker's message content
```

**Edge Cases**:

- Worker project with no InvokeJS calls — should still initialize and post `uno-worker-ready`
- Host with multiple workers — each Worker instance gets its own .NET runtime (~23 MB each)
- Worker that throws during Main — error should propagate to host's `worker.onerror`

### P2: Standalone Worker (No Host)

**User Journey**: A developer uses the `WebWorker` mode without a host project. The build produces `worker.js` and a minimal `index.html` host page for testing. The developer serves the output directly.

**Priority Justification**: Enables rapid worker development and testing without a separate host project.

**Independent Test Approach**: Publish the worker project standalone, serve it, and verify via Puppeteer that the worker initializes.

**Acceptance Scenarios**:

```gherkin
Given a project with WasmShellMode=WebWorker
When the developer runs dotnet publish
Then the output SHALL contain worker.js and index.html
And index.html SHALL create a Worker pointing to worker.js
And the worker SHALL initialize the .NET runtime
And the worker SHALL post { type: 'uno-worker-ready' } on success
```

### P3+: Custom Worker Filename

**User Journey**: A developer sets `WasmShellWorkerFileName=my-worker.js` to customize the output filename.

**Acceptance Scenarios**:

```gherkin
Given a project with WasmShellWorkerFileName=my-worker.js
When published
Then the output SHALL contain my-worker.js instead of worker.js
And index.html SHALL reference my-worker.js
```

## Requirements

### Functional Requirements

**FR-1**: The `ShellMode` enum SHALL include a `WebWorker` value.

**FR-2**: When `WasmShellMode=WebWorker`, the build SHALL generate a `worker.js` (or custom filename) that:

- Loads `uno-config.js` via fetch (stripping ES module export syntax for classic worker compatibility)
- Sets up `globalThis.Uno.WebAssembly.Bootstrap.Bootstrapper.invokeJS` shim for `[JSImport]` interop
- Dynamically imports `dotnet.js` from `_framework/`
- Configures the .NET runtime with environment variables and runtime options
- Calls `runMain` after runtime initialization
- Posts `{ type: 'uno-worker-ready' }` on success
- Does NOT set `self.onmessage` before `dotnet.create()` (critical: runtime checks `globalThis.onmessage` to detect pthread deputies)

**FR-3**: When `WasmShellMode=WebWorker`, the build SHALL also generate a minimal `index.html` host page that creates the worker and displays status/messages.

**FR-4**: When `WasmShellMode=WebWorker`, Hot Reload SHALL be automatically disabled (the `Uno.Wasm.MetadataUpdater` assembly throws `MissingMethodException` in worker context).

**FR-5**: When `WasmShellMode=WebWorker`, the service worker (PWA) SHALL NOT be generated.

**FR-6**: The `WasmShellWorkerFileName` MSBuild property SHALL control the output filename (default: `worker.js`).

### Host Integration Requirements

**FR-7**: The host project SHALL set `WasmShellWebWorkerProject` to the worker `.csproj` path and `WasmShellWorkerBasePath` (default: `_worker`) to control the output subdirectory.

**FR-8**: During the host's build, the `_UnoBuildAndImportWebWorkerAssets` target SHALL:

- Build the worker project via `MSBuild` task
- Read the worker's static web assets via `GetCurrentProjectBuildStaticWebAssetItems`
- Register manifest assets (worker.js, package_*, etc.) as `StaticWebAsset` items in the host with `BasePath=_worker` and cleared `WasmResource`/`Culture` traits
- Register `_framework/` files from the worker's build output via `DefineStaticWebAssets`
- The standard SDK publish pipeline copies everything to the correct output paths

**FR-9**: The host project SHALL NOT use `<ProjectReference>` to the worker project for assembly referencing. The `WasmShellWebWorkerProject` property and the `_UnoBuildAndImportWebWorkerAssets` target handle the integration, bypassing the SDK's static web asset conflict detection by using a distinct `SourceId` and clearing `WasmResource` traits.

### Key Entities

**Worker Bootstrap Script** (`worker.js`): A self-contained JavaScript file generated by `GenerateWorkerJs()` in `ShellTask.cs` that initializes the .NET runtime in a Web Worker context. Uses relative paths from `self.location` to find `uno-config.js` and `_framework/`.

**Host Page** (`index.html`): A minimal HTML page generated alongside the worker script for standalone testing. Creates the worker and displays message events.

**Worker Base Path** (`_worker`): The subdirectory under the host's wwwroot where the worker's complete output (including its own `_framework/`) is placed.

## Implementation

### Modified Files

**`src/Uno.Wasm.Bootstrap/ShellMode.cs`**: Added `WebWorker` to the enum.

**`src/Uno.Wasm.Bootstrap/ShellTask.cs`**:

- Added `WorkerFileName` property (default: `worker.js`)
- Added `GenerateWorkerJs()` method — generates the worker bootstrap script and host page, called from `Execute()` between `GenerateEmbeddedJs()` and `GenerateIndexHtml()`
- `BuildServiceWorker()` returns early for `WebWorker` mode (no PWA service worker)

**`src/Uno.Wasm.Bootstrap/build/Uno.Wasm.Bootstrap.targets`**:

- Added `WasmShellWorkerFileName` property (default: `worker.js`)
- Added `WasmShellWorkerBasePath` property (default: `_worker`)
- Passes `WorkerFileName` to `ShellTask_v0`
- Hot Reload injection skipped for `WasmShellMode=WebWorker`
- Added `_UnoPublishWebWorkerProject` target for host integration

### Created Files

**`src/Uno.Wasm.Tests.WebWorker.App/`**: WebWorker-mode sample project. Posts "Hello from .NET WebWorker" message via `InvokeJS`.

**`src/Uno.Wasm.Tests.WebWorker.Host/`**: Browser-mode host project. Sets `WasmShellWebWorkerProject` and `WasmShellWorkerBasePath`. Creates worker at `_worker/worker.js` from `Program.cs`, displays received messages.

**`src/Uno.Wasm.Tests.WebWorker/`**: Test harness with `test-webworker.sh` (CI script) and `validate-webworker.js` (Puppeteer test).

**`doc/features-webworker-mode.md`**: User-facing documentation.

**`build/ci/stage-build-linux-tests.yml`**: WebWorker test added to linux02 CI job.

## Design Decisions

### Worker JS Generated by C# (Not TypeScript)

The worker bootstrap is generated by `GenerateWorkerJs()` in `ShellTask.cs` (following the `GenerateEmbeddedJs()` pattern) rather than compiled from TypeScript. This avoids coupling with the DOM-heavy `Bootstrapper.ts` and keeps the worker script self-contained.

### Classic Worker (Not Module Worker)

The generated worker uses a classic worker (`new Worker(url)`) rather than a module worker (`new Worker(url, { type: 'module' })`). This is required because the Emscripten runtime detects `importScripts` to identify the worker environment.

### Config Loading via fetch+eval

The `uno-config.js` file uses ES module `export` syntax, which is incompatible with `importScripts()` in classic workers. The worker script fetches the config via `fetch()`, strips the `export` statement, replaces `let config` with `self.config` to escape the `new Function()` scope, and evaluates it.

### Manifest-Based Host Integration

The host integration uses the SDK's `GetCurrentProjectBuildStaticWebAssetItems` target to read the worker's build manifest, then re-registers the assets in the host's pipeline under `_worker/`. Five approaches were tried before arriving at this:

1. `StaticWebAssetBasePath` on worker — `_framework/` conflict in build manifest
2. `StaticWebAssetProjectMode=Default` — assets not copied during publish
3. `DefineStaticWebAssets` on raw file glob — `RelativePath` conflict ignoring `BasePath`
4. Explicit `dotnet publish` + file copy — worked but required `RemoveDir` and shelling out
5. **Manifest-based** (current): read manifest, re-register with distinct `SourceId`/`BasePath`, clear `WasmResource` traits, glob `_framework/` separately via `DefineStaticWebAssets`

The key insight: by using a different `SourceId` and clearing `WasmResource`/`Culture` traits, the worker's assets flow through the host's pipeline without triggering the Blazor SDK's boot JSON generator or conflict detection. The `_framework/` files require a separate `DefineStaticWebAssets` call because they're generated by the SDK's WASM pipeline outside the manifest.

### `self.onmessage` Never Set Before Runtime Init

The .NET runtime checks `globalThis.onmessage` during startup to detect whether it's running as a pthread deputy worker. If set, the runtime skips asset promise resolution and hangs. The worker script uses `self.addEventListener("message", ...)` only after full initialization, and the `uno-worker-ready` message is posted after `runMain` completes.

## Success Criteria

**SC-1**: Publishing a `WasmShellMode=WebWorker` project SHALL produce `worker.js`, `index.html`, `uno-config.js`, and `_framework/` with the .NET runtime.

**SC-2**: Publishing a host project with `WasmShellWebWorkerProject` set SHALL produce the host's output at `/` and the worker's output at `/_worker/`, each with its own `_framework/`.

**SC-3**: The host+worker Puppeteer test SHALL pass: host creates worker, worker initializes .NET runtime, worker posts message, host receives and displays "Hello from .NET WebWorker".

**SC-4**: The worker SHALL NOT access DOM APIs (`document`, `window`, etc.).

**SC-5**: The linux02 CI job SHALL run the WebWorker test and pass.

## Out of Scope

- WebAssembly.Module reuse between host and worker (see `WorkerFork.ts` in specs/003-worker-fork/)
- Bidirectional structured messaging API (beyond raw `postMessage`)
- Worker pool management
- Shared memory / `SharedArrayBuffer` integration
- Dev-server hot reload for worker code changes
- AOT compilation in the worker during host publish (worker uses build output; AOT requires separate publish)
