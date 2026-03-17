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

   This generates a `worker.js` file instead of the standard `index.html` bootstrapper.

2. Optionally, customize the worker filename:

   ```xml
   <WasmShellWorkerFileName>my-worker.js</WasmShellWorkerFileName>
   ```

   The default filename is `worker.js`.

## How it works

When published, the bootstrapper generates:

- **`worker.js`** (or your custom filename) — A self-contained script that initializes the .NET runtime inside a Web Worker. It loads the configuration, sets up the runtime, and runs the application's `Main` method.
- **`index.html`** — A minimal host page that creates the worker and displays status. This is provided for development and testing; in production, you would create the worker from your own host page.

The generated `worker.js`:

1. Fetches and parses `uno-config.js` for runtime configuration
2. Dynamically imports `dotnet.js` from `_framework/`
3. Configures environment variables and runtime options
4. Runs the application's `Main` method
5. Posts `{ type: "uno-worker-ready" }` to signal successful initialization

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

## Important notes

- The worker runs in its own thread with no access to the DOM. All DOM interactions must happen on the main thread.
- Each worker instance loads its own .NET runtime (~23 MB private memory).
- The generated worker is a [classic worker](https://developer.mozilla.org/en-US/docs/Web/API/Worker/Worker) (not a module worker), which is required for compatibility with the Emscripten runtime.
- **Hot Reload is automatically disabled** in WebWorker mode, as it relies on browser APIs not available in workers.
- **Service Worker (PWA) is not generated** in WebWorker mode, since service workers are a separate concept from web workers.
- For cross-origin isolation (required for `SharedArrayBuffer` and threading), ensure your server sends the appropriate headers:
  ```
  Cross-Origin-Embedder-Policy: require-corp
  Cross-Origin-Opener-Policy: same-origin
  ```

## Hosting in another project

The WebWorker project is designed to be consumed by a host app. The host project sets two MSBuild properties to integrate the worker's output:

```xml
<PropertyGroup>
    <!-- Path to the WebWorker .csproj -->
    <WasmShellWebWorkerProject>..\MyWorker\MyWorker.csproj</WasmShellWebWorkerProject>
    <!-- Subdirectory under wwwroot/ where worker files are placed (default: _worker) -->
    <WasmShellWorkerBasePath>_worker</WasmShellWorkerBasePath>
</PropertyGroup>
```

During `dotnet publish`, the build system automatically:
1. Publishes the worker project
2. Copies its output (including its own `_framework/`) into `wwwroot/_worker/`
3. Fixes up the `dotnet.js` fingerprint in the worker's `uno-config.js`

The host then creates a Worker pointing to the worker's base path:

```javascript
const worker = new Worker('./_worker/worker.js');
```

> [!NOTE]
> Do **not** use a `<ProjectReference>` to the worker project for assembly referencing. Both projects produce their own `_framework/` directory with .NET runtime assemblies, which causes static web asset conflicts. Instead, use `WasmShellWebWorkerProject` to point to the worker project.

## MSBuild properties

| Property | Default | Description |
|---|---|---|
| `WasmShellMode` | `browser` | Set to `WebWorker` to enable worker mode |
| `WasmShellWorkerFileName` | `worker.js` | Name of the generated worker script file |
| `WasmShellWebWorkerProject` | _(empty)_ | Path to a WebWorker project to publish alongside the host |
| `WasmShellWorkerBasePath` | `_worker` | Subdirectory for the worker's output in the host's wwwroot |
