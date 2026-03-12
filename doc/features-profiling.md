---
uid: Uno.Wasm.Bootstrap.Profiling.Memory
---
# Memory Profiling

Managed Memory profiling is available through the use of the [Xamarin Profiler](https://docs.microsoft.com/en-us/xamarin/tools/profiler) or [`mprof-report`](https://www.mono-project.com/docs/debug+profile/profile/profiler/#analyzing-the-profile-data).

Memory snapshots are supported to enable memory difference analysis and find memory leaks.

## Using the memory profiler

- Add the following to your csproj:

  ```xml
  <PropertyGroup>
    <WasmShellEnableLogProfiler>true</WasmShellEnableLogProfiler>
  </PropertyGroup>
  ```

Once the application is running:

- Use the `Shift+Alt+H` (`Shift+Cmd+H` on macOS) hotkey to create memory snapshots
- Use the `Shift+Alt+P` (`Shift+Cmd+P` on macOS) hotkey to save the profile to a file

With the saved `mlpd` file, you can either:

- Open it with the Xamarin Profiler (which needs to be explicitly installed in the VS 2022 installer)
- Show a memory report using `mprof-report`. The tool can be installed under Ubuntu WSL using `sudo apt install mono-utils`

### Additional profiler configuration

The profiler uses the [official configuration string](https://www.mono-project.com/docs/debug+profile/profile/profiler/#profiler-option-documentation), which can be adjusted using the following options:

```xml
<PropertyGroup>
  <WasmShellEnableLogProfiler>true</WasmShellEnableLogProfiler>
  <WasmShellLogProfilerOptions>log:alloc,output=output.mlpd</WasmShellLogProfilerOptions>
</PropertyGroup>
```

Note that as of .NET 6:

- The support for any timer based profiling is not supported, because of the lack of threading support in .NET
- The support for remote control of the profiler is not supported

## Memory Layout of a WebAssembly app

A WebAssembly app has multiple categories of allocated memory:

- The managed memory usage, which is the smallest portion of the overall used memory. This is the memory directly done by object allocations from managed code. It is the memory reported by `mprof-report` or the Xamarin Profiler. This memory can be queried by using the [`GC.GetTotalMemory`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalmemory).
- The WebAssembly module memory usage, which is the overall app memory usage as far as WebAssembly is concerned. This is similar to the committed memory in a Windows app. This contains:
  - The runtime's own native memory allocations
  - The Garbage Collector memory, which is [directly impacted](xref:Uno.Development.Performance#webassembly-specifics) by the [MONO_GC_PARAMS parameters](https://github.com/dotnet/runtime/blob/0bfb733c6419e78e55286e0d01c5994a337c486a/docs/design/mono/mono-manpage-1.md#environment-variables). Note that the garbage collector over-allocates WebAssembly memory for performance reasons.
  - All the files located in the `Package_xxxx/managed` folder. Each assembly file is directly loaded in memory by the .NET runtime, regardless of it being used at the app's startup or not. Reducing the assemblies file sizes can be acheived by configuring the [IL Linker](xref:uno.articles.features.illinker).
  
  The WebAssembly memory can be queried using the following JavaScript expression: `globalThis.Module.HEAPU8.length`, which gives the allocated memory in bytes. You can use [`JSImport`](xref:Uno.Wasm.Bootstrap.JSInterop#invoking-javascript-code-from-c) to read it from managed code.
- The javascript memory, which is mostly about the HTML DOM rendering and the browser's own runtime memory. Those two categories can be analyzed using the [browser's own debugging tooling](https://developer.chrome.com/docs/devtools/memory-problems).

## CPU Profiling

### Browser Performance Tab (Emscripten function names)

To enable the profiling of the WebAssembly code, set the following parameter:

```xml
<WasmShellEnableEmccProfiling>true</WasmShellEnableEmccProfiling>
```

This will ensure that the toolchain keeps the function names so that the browser shows meaningful information in the **Performance** tab.

### Browser Profiler (Mono samplepoint instrumentation)

> [!NOTE]
> Requires .NET 10 or later.

The Mono browser profiler enables stack trace sampling in single-threaded WebAssembly by inserting sample points at interpreter safepoints, without requiring a dedicated sampling thread.

Enable it in your project file:

```xml
<PropertyGroup>
    <WasmShellEnableBrowserProfiler>true</WasmShellEnableBrowserProfiler>
</PropertyGroup>
```

Then configure the sampling interval in your JavaScript initialization:

```javascript
dotnet.withConfig({
    browserProfilerOptions: { sampleIntervalMs: 10 }
})
```

The default interval is 1000 ms. Lower values give finer granularity but add more overhead.

To restrict profiling to a specific namespace, set `WasmShellBrowserProfilerCallSpec`:

```xml
<PropertyGroup>
    <WasmShellEnableBrowserProfiler>true</WasmShellEnableBrowserProfiler>
    <WasmShellBrowserProfilerCallSpec>N:MyApp.MyNamespace</WasmShellBrowserProfilerCallSpec>
</PropertyGroup>
```

See the [Mono diagnostics tracing documentation](https://github.com/dotnet/runtime/blob/main/docs/design/mono/diagnostics-tracing.md#trace-monovm-profiler-events-during-startup) for the full `callspec` syntax.

> [!NOTE]
> This profiler targets the interpreter. It does not support multi-threaded environments.

### EventPipe CPU Sampling and Diagnostics

> [!NOTE]
> Requires .NET 10 or later.

EventPipe diagnostics provide gc dumps, CPU samples, and performance counters from within the browser, using the same pipeline as desktop .NET diagnostic tools.

Enable it in your project file:

```xml
<PropertyGroup>
    <WasmShellEnablePerfTracing>true</WasmShellEnablePerfTracing>
    <!-- Restrict instrumentation to specific namespaces (optional) -->
    <WasmShellPerfInstrumentation>N:MyApp.MyNamespace</WasmShellPerfInstrumentation>
</PropertyGroup>
```

Once the application is running, trigger collection from the browser console:

```javascript
// Collect a GC dump
globalThis.getDotnetRuntime(0).collectGcDump()

// Collect performance counters for 60 seconds
globalThis.getDotnetRuntime(0).collectPerfCounters({ durationSeconds: 60 })

// Collect CPU samples for 60 seconds
globalThis.getDotnetRuntime(0).collectCpuSamples({ durationSeconds: 60 })
```

Each call downloads a `.nettrace` or `.gcdump` file that can be opened with [PerfView](https://github.com/microsoft/perfview) or [Visual Studio's diagnostic tools](https://learn.microsoft.com/en-us/visualstudio/profiling).

#### Remote collection via ds-router

You can also connect standard .NET diagnostic CLI tools to a running browser app through a WebSocket relay. Set the diagnostic port in your project file:

```xml
<PropertyGroup>
    <WasmShellDiagnosticPorts>ws://127.0.0.1:8088/diagnostics</WasmShellDiagnosticPorts>
</PropertyGroup>
```

Then on your host machine:

```bash
# Start the WebSocket relay
dotnet-dsrouter server-websocket -ws http://127.0.0.1:8088/diagnostics

# In separate terminals, attach the desired tool (use the PID printed by ds-router)
dotnet-gcdump collect -p <pid>
dotnet-trace collect -p <pid>
dotnet-counters collect -p <pid>
```

Install the tools if needed:

```bash
dotnet tool install --global dotnet-dsrouter
dotnet tool install --global dotnet-gcdump
dotnet tool install --global dotnet-trace
dotnet tool install --global dotnet-counters
```

## MSBuild Properties Reference

| Property | Type | Description |
|----------|------|-------------|
| `WasmShellEnableLogProfiler` | `bool` | Enables the Mono log profiler for managed memory profiling. Saves `.mlpd` files. |
| `WasmShellLogProfilerOptions` | `string` | Profiler option string passed to the Mono log profiler (e.g. `log:alloc,output=output.mlpd`). |
| `WasmShellEnableEmccProfiling` | `bool` | Preserves WebAssembly function names so the browser Performance tab shows readable stacks. |
| `WasmShellEnableBrowserProfiler` | `bool` | Enables the Mono browser profiler (samplepoint instrumentation). Requires .NET 10+. |
| `WasmShellBrowserProfilerCallSpec` | `string` | Optional `callspec` filter for the browser profiler (e.g. `N:MyApp.MyNamespace`). |
| `WasmShellEnablePerfTracing` | `bool` | Enables EventPipe diagnostics: GC dumps, CPU samples, and performance counters. Requires .NET 10+. |
| `WasmShellPerfInstrumentation` | `string` | Optional namespace filter for EventPipe instrumentation (e.g. `N:MyApp.MyNamespace`). |
| `WasmShellDiagnosticPorts` | `string` | Sets `DOTNET_DiagnosticPorts` for remote collection via `dotnet-dsrouter` (e.g. `ws://127.0.0.1:8088/diagnostics`). |
