---
uid: UnoWasmBootstrap.Features.WebWorkerMode
---

# WebWorker mode

By default, the project is launched with a HTML page (`index.html`) in the browser. The WebWorker mode allows running the .NET application inside a [Web Worker](https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API), enabling background execution without blocking the main UI thread.

## Setup

1. Set the shell mode in your project file:

   ```xml
   <WasmShellMode>WebWorker</WasmShellMode>
   ```

   This generates a `worker.js` bootstrapper file and a minimal `index.html` host page for testing.

2. Optionally, customize the worker filename:

   ```xml
   <WasmShellWorkerFileName>my-worker.js</WasmShellWorkerFileName>
   ```

   The default filename is `worker.js`.

## How it works

When published, the bootstrapper generates:

- **`worker.js`** (or your custom filename) — A compiled TypeScript bootstrapper (`WorkerBootstrapper.ts`) that initializes the .NET runtime inside a Web Worker. It loads the configuration, sets up the runtime, configures profilers, and runs the application's `Main` method.
- **`index.html`** — A minimal host page that creates the worker and displays status. This is provided for development and testing; in production, you would create the worker from your own host page.

The generated `worker.js`:

1. Fetches and parses `uno-config.js` for runtime configuration
2. Sets up the `invokeJS` shim for `[JSImport]` interop
3. Dynamically imports `dotnet.js` from `_framework/`
4. Configures environment variables, runtime options, and profilers (log, AOT, memory)
5. Runs the application's `Main` method
6. Posts `{ type: "uno-worker-ready" }` to signal successful initialization
7. Registers a profiler command handler for host-initiated profiler data retrieval

## Using the worker from a host page

In your host HTML page, create the worker and listen for messages:

```html
<script>
    const worker = new Worker('./worker.js');

    worker.addEventListener('message', function(e) {
        if (e.data.type === 'uno-worker-ready') {
            console.log('Worker runtime initialized');
        }
        if (e.data.type === 'dotnet-ready') {
            console.log('Worker app started:', e.data.message);
        }
    });

    worker.addEventListener('error', function(e) {
        console.error('Worker error:', e.message);
    });
</script>
```

## Communicating with the worker

From the .NET `Main` method, you can post messages back to the host using JavaScript interop:

```csharp
using System.Runtime.InteropServices.JavaScript;

public static partial class Interop
{
    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.Bootstrapper.invokeJS")]
    public static partial string InvokeJS(string value);
}

// Post a message to the host page
Interop.InvokeJS("self.postMessage({ type: 'dotnet-ready', message: 'Hello from worker' })");
```

To receive messages from the host page, register a handler **after** the runtime has initialized (i.e., inside `Main` or later):

```csharp
// Register a message handler in Main
Interop.InvokeJS(@"
    self.addEventListener('message', function(e) {
        console.log('Worker received:', e.data);
    });
");
```

> [!IMPORTANT]
> Do **not** set `self.onmessage` before the .NET runtime has initialized. The .NET runtime checks `globalThis.onmessage` during startup to detect worker types, and setting it prematurely will cause the runtime to hang. Always use `self.addEventListener('message', ...)` instead.

## Profiling in workers

Workers cannot use DOM-based hotkeys or file downloads. Instead, the worker bootstrapper exposes global helper functions that you can call directly from the **worker's DevTools console**. The profiler data is posted to the host page via `postMessage`, which automatically triggers a file download.

To access the worker console, open the browser DevTools, go to **Sources** (Chrome) or **Debugger** (Firefox), find the worker in the sidebar, and switch to its console context.

### Memory profiler

Enable the memory profiler on the worker project:

```xml
<WasmShellEnableWasmMemoryProfiler>true</WasmShellEnableWasmMemoryProfiler>
```

Then in the worker's console:

```javascript
saveMemoryProfile()            // speedscope format (default)
saveMemoryProfile("perfview")  // PerfView format
```

### Log profiler

Enable the log profiler on the worker project:

```xml
<WasmShellEnableLogProfiler>true</WasmShellEnableLogProfiler>
```

Then in the worker's console:

```javascript
saveLogProfile()
```

### AOT profiler

Enable AOT profiling on the worker project:

```xml
<WasmShellGenerateAOTProfile>true</WasmShellGenerateAOTProfile>
```

Then in the worker's console:

```javascript
saveAotProfile()
```

When the worker initializes with profilers enabled, it prints the available commands to the console:

```text
[WorkerProfiler] Available profiler commands (run from this worker console):
  saveMemoryProfile("speedscope") or saveMemoryProfile("perfview")
  saveLogProfile()
```

### How it works

The worker posts profiler data to the host via `postMessage` with type `uno-profiler-data`. The generated standalone host page (`index.html`) automatically handles these messages and triggers file downloads. If you use your own host page, add this handler:

```javascript
worker.addEventListener('message', function(e) {
    if (e.data && e.data.type === 'uno-profiler-data') {
        var bytes = Uint8Array.from(atob(e.data.data), function(c) { return c.charCodeAt(0); });
        var a = document.createElement('a');
        a.href = URL.createObjectURL(new Blob([bytes]));
        a.download = e.data.filename;
        a.click();
        URL.revokeObjectURL(a.href);
    }
});
```

The host can also programmatically request profiler data:

```javascript
worker.postMessage({ type: 'uno-profiler-command', command: 'memory-snapshot', format: 'speedscope' });
worker.postMessage({ type: 'uno-profiler-command', command: 'log-profiler-save' });
worker.postMessage({ type: 'uno-profiler-command', command: 'aot-profiler-save' });
```

## Important notes

- The worker runs in its own thread with no access to the DOM. All DOM interactions must happen on the main thread.
- Each worker instance loads its own .NET runtime (~23 MB private memory).
- The generated worker is a [classic worker](https://developer.mozilla.org/en-US/docs/Web/API/Worker/Worker) (not a module worker), which is required for compatibility with the Emscripten runtime.
- **Hot Reload is automatically disabled** in WebWorker mode, as it relies on browser APIs not available in workers.
- **Service Worker (PWA) is not generated** in WebWorker mode, since service workers are a separate concept from web workers.
- For cross-origin isolation (required for `SharedArrayBuffer` and threading), ensure your server sends the appropriate headers:

  ```text
  Cross-Origin-Embedder-Policy: require-corp
  Cross-Origin-Opener-Policy: same-origin
  ```

## Hosting in another project

The WebWorker project is designed to be consumed by a host app. The host project sets two MSBuild properties to integrate the worker's output:

```xml
<PropertyGroup>
    <!-- Path to the WebWorker .csproj -->
    <WasmShellWebWorkerProject>..\MyWorker\MyWorker.csproj</WasmShellWebWorkerProject>
    <!-- Optional sub-folder under the host's package_<hostHash>/ folder
         (default: 'worker'). -->
    <WasmShellWorkerBasePath>worker</WasmShellWorkerBasePath>
</PropertyGroup>
```

During build and publish, the build system automatically:

1. Builds the worker project and registers its assets as host `Content` items rooted under `<WasmShellWorkerBasePath>` so they flow through Bootstrap's package-folder pipeline
2. Publishes the worker's `_framework/` directory and copies it into `wwwroot/package_<hostHash>/<WasmShellWorkerBasePath>/_framework/`
3. Fixes up the `dotnet.js` fingerprint in the worker's `uno-config.js`

The worker is placed inside the host's hashed package folder so the worker URL is implicitly versioned by the host's content hash. This prevents v1-host pages from silently pairing with a v2 worker during a rolling deployment — the URL of the worker either resolves to the matching version that's still on disk, or 404s.

The host resolves the worker URL via `config.uno_app_base` (which includes the hash):

```javascript
const appBase = (globalThis.config && globalThis.config.uno_app_base) || '.';
const worker = new Worker(appBase + '/worker/worker.js');
```

### Sharing code via linked files

The worker project can reuse code from the host or a shared project using linked files instead of duplicating source:

```xml
<!-- In the worker .csproj -->
<ItemGroup>
    <Compile Include="..\MyShared\Benchmark.cs" Link="Benchmark.cs" />
</ItemGroup>
```

This is the recommended approach for scenarios like running the same computation on both the main thread and a worker (e.g., side-by-side ray tracer comparison).

## MSBuild properties

| Property | Default | Description |
|---|---|---|
| `WasmShellMode` | `browser` | Set to `WebWorker` to enable worker mode |
| `WasmShellWorkerFileName` | `worker.js` | Name of the generated worker script file |
| `WasmShellWebWorkerProject` | _(empty)_ | Path to a WebWorker project to publish alongside the host |
| `WasmShellWorkerBasePath` | `worker` | Sub-folder under the host's `package_<hostHash>/` folder. The worker URL is therefore versioned by the host's content hash. |
