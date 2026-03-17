---
uid: UnoWasmBootstrap.Features.WorkerFork
---

# Worker Fork

The Worker Fork feature allows you to spawn a new .NET WebAssembly runtime instance in a Web Worker from the main thread. This enables running background .NET code in a separate thread without blocking the UI, while reusing the already-compiled WebAssembly module from the main thread for fast startup.

## Enabling the feature

Add the following MSBuild property to your project file:

```xml
<PropertyGroup>
    <WasmShellEnableWorkerFork>true</WasmShellEnableWorkerFork>
</PropertyGroup>
```

This enables the `WebAssembly.Module` capture during runtime initialization and makes the `WorkerFork` API available. Without this property, calling `fork()` or `forkToWorker()` will throw an error.

## How it works

When you call `fork()`, the bootstrapper:

1. Takes the pre-compiled `WebAssembly.Module` captured during the main thread's runtime initialization
2. Creates a classic Web Worker from an inline blob script
3. Sends the compiled module and configuration to the worker via `postMessage`
4. The worker imports `dotnet.js`, intercepts `WebAssembly.compileStreaming` to reuse the pre-compiled module (skipping re-download and re-compilation), and initializes a fresh .NET runtime
5. The worker calls `Main()` with the provided arguments, allowing the same entry point to detect whether it's running on the main thread or in a worker

## Usage with `[JSImport]` / `[JSExport]`

The recommended approach uses .NET's `[JSImport]` and `[JSExport]` attributes for type-safe, CSP-compliant interop without `eval`.

### Main thread interop

Define `[JSImport]` bindings for the WorkerFork bridge methods:

```csharp
using System.Runtime.InteropServices.JavaScript;

internal static partial class MainInterop
{
    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.fork")]
    public static partial void Fork(
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] args);

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.sendMessage")]
    public static partial void SendMessage(string json);

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.terminateWorker")]
    public static partial void TerminateWorker();

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.setOnMessageCallback")]
    public static partial void SetOnMessageCallback(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);

    [JSImport("globalThis.Uno.WebAssembly.Bootstrap.WorkerFork.setOnErrorCallback")]
    public static partial void SetOnErrorCallback(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);
}
```

### Worker-side interop

In the worker, the bootstrapper provides global functions for sending messages and registering callbacks:

```csharp
internal static partial class WorkerInterop
{
    [JSImport("globalThis.__unoWorkerPostMessage")]
    public static partial void PostMessage(string json);

    [JSImport("globalThis.__unoWorkerSetMessageCallback")]
    public static partial void RegisterMessageCallback(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);
}
```

### Complete example

```csharp
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    static void Main(string[] args)
    {
        var isWorker = Array.Exists(args, a => a == "--worker")
            || Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_IS_WORKER") == "true";

        if (isWorker)
            RunWorker();
        else
            RunMain();
    }

    static void RunMain()
    {
        // Register callbacks first, then fork.
        MainInterop.SetOnMessageCallback(OnWorkerMessage);
        MainInterop.SetOnErrorCallback(OnWorkerError);
        MainInterop.Fork(["--worker"]);
    }

    [JSExport]
    public static void OnWorkerMessage(string json)
    {
        // The bridge sends a special "__workerReady" message when the worker is initialized.
        if (json.Contains("\"__workerReady\":true"))
        {
            MainInterop.SendMessage("{\"text\":\"hello from main\"}");
            return;
        }

        Console.WriteLine($"Received from worker: {json}");
    }

    [JSExport]
    public static void OnWorkerError(string error) =>
        Console.Error.WriteLine($"Worker error: {error}");

    static void RunWorker()
    {
        // Register the message handler via [JSImport].
        WorkerInterop.RegisterMessageCallback(OnMainThreadMessage);
    }

    [JSExport]
    public static void OnMainThreadMessage(string json)
    {
        // Echo back with modifications.
        var response = $"{{\"echo\":true,\"original\":{json}}}";
        WorkerInterop.PostMessage(response);
    }
}
```

## JavaScript API

For advanced scenarios, the low-level `forkToWorker()` method is also available:

```javascript
var handle = Uno.WebAssembly.Bootstrap.WorkerFork.forkToWorker({
    args: ['--worker'],
    onMessage: function(data) {
        console.log('Received from worker:', data);
    },
    onError: function(err) {
        console.error('Worker error:', err);
    }
});

handle.ready.then(function() {
    handle.postMessage({ text: 'hello' });
});
```

The method returns a `WorkerHandle` with:

| Property/Method | Description |
|---|---|
| `ready` | A `Promise<void>` that resolves when the worker's .NET runtime is initialized |
| `postMessage(data)` | Sends a message to the worker |
| `terminate()` | Terminates the worker |

## Bridge methods

The `WorkerFork` class exposes `[JSImport]`-friendly static methods:

| Method | Description |
|---|---|
| `fork(args)` | Fork to a worker with the given arguments |
| `sendMessage(json)` | Send a JSON message to the active worker |
| `terminateWorker()` | Terminate the active worker |
| `setOnMessageCallback(fn)` | Register a callback for worker messages (JSON strings) |
| `setOnErrorCallback(fn)` | Register a callback for worker errors |

On the worker side:

| Function | Description |
|---|---|
| `globalThis.__unoWorkerPostMessage(json)` | Send a JSON message back to the main thread |
| `globalThis.__unoWorkerSetMessageCallback(fn)` | Register a callback for messages from the main thread. Drains any pending messages. |

## WebAssembly.Module reuse

The main thread captures the compiled `WebAssembly.Module` during runtime initialization (via `installWasmModuleCapture()` which wraps `WebAssembly.compile` and `WebAssembly.compileStreaming`). This module is stored on `globalThis.__unoWasmModule` and transferred to the worker via `postMessage`.

In the worker, the module reuse works through two mechanisms:

1. **Compilation interception**: The worker wraps `WebAssembly.compileStreaming` to return the pre-compiled module instead of re-compiling from the downloaded binary.
2. **Download skip**: The worker uses `withResourceLoader` to return an empty response for the `dotnetwasm` resource, avoiding the ~8 MB download of `dotnet.native.wasm`.

Each worker still allocates its own WASM linear memory and .NET managed heap (~23 MB per instance), but startup is significantly faster since WASM compilation is skipped.

## Server requirements

The Worker Fork feature requires the following HTTP headers on your server for `SharedArrayBuffer` support:

```http
Cross-Origin-Embedder-Policy: require-corp
Cross-Origin-Opener-Policy: same-origin
```

See the [Threading documentation](features-threading.md) for more details on these headers.

## Restrictions

- The worker runs a **separate .NET runtime instance** with its own memory. Objects cannot be shared directly between the main thread and worker; all communication is via JSON messages.
- Each worker adds approximately **23 MB** of private memory for the .NET runtime (WASM linear memory + managed heap + assembly metadata). This is the irreducible cost regardless of module reuse.
- The worker uses a **classic worker** (not a module worker) because the Emscripten runtime checks `typeof importScripts === "function"` for worker detection.
- `self.onmessage` must **not** be set before .NET runtime initialization completes, as the .NET runtime checks `globalThis.onmessage` to detect pthread deputy workers. The bootstrapper uses `addEventListener("message", ...)` instead.
- The `fork()` / `forkToWorker()` method requires the .NET runtime to be fully initialized on the main thread before it can be called (it reads `globalThis.__unoWasmModule` and `globalThis.config`).
