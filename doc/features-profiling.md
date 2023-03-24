---
uid: Uno.Wasm.Bootstrap.Profiling.Memory
---
## Memory Profiling
Managed Memory profiling is available through the use of the [Xamarin Profiler](https://docs.microsoft.com/en-us/xamarin/tools/profiler) or [`mprof-report`](https://www.mono-project.com/docs/debug+profile/profile/profiler/#analyzing-the-profile-data).

Memory snapshots are supported to enable memory difference analysis and find memory leaks.

### Using the memory profiler

- Add the following to your csproj 
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

## CPU Profiling 

To enable the profiling of the WebAssembly code, set te following parameter:

```xml
<WasmShellEnableEmccProfiling>true</WasmShellEnableEmccProfiling>
```

This will ensure that the toolchain keeps the function names so that the browser shows meaningful information in the **Performance** tab.

