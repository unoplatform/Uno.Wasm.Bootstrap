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

To enable the profiling of the WebAssembly code, set te following parameter:

```xml
<WasmShellEnableEmccProfiling>true</WasmShellEnableEmccProfiling>
```

This will ensure that the toolchain keeps the function names so that the browser shows meaningful information in the **Performance** tab.
