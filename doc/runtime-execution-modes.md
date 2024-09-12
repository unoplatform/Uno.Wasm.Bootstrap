---
uid: Uno.Wasm.Bootstrap.Runtime.Execution
---

# Runtime Execution Modes

The mono for WebAssembly runtime provides three execution modes, Interpreter, and Mixed Mode Interpreter/AOT (Ahead of Time).

The execution mode can be set as follows:

```xml
<WasmShellMonoRuntimeExecutionMode>Interpreter</WasmShellMonoRuntimeExecutionMode>
```

The possible values are:

- `Interpreter` (the default mode)
- `InterpreterAndAOT`

## Interpreter mode

This mode is the slowest but allows for great flexibility and debugging, as well as an efficient payload size.

The linker mode can also be completely disabled for troubleshooting, as this will not impact the wasm payload size.

## Jiterpreter mode

This mode is a hybrid between the interpreter and the AOT modes, where the interpreter is used to execute the code, but the JIT engine is used to generate some WebAssembly code on the fly. This mode is generally faster than the interpreter, but slower than the AOT mode.

To enable this mode, use the following option:

```xml
<PropertyGroup>
    <WasmShellEnableJiterpreter>true</WasmShellEnableJiterpreter>
</PropertyGroup>
```

Additionally, some options can be used to fine-tune the Jiterpreter mode, using options found [in this file](https://github.com/dotnet/runtime/blob/6a047a9aec7a36039cffac61186b04bd3f16dbe0/src/mono/mono/utils/options-def.h#L86-L114):

```xml
<PropertyGroup>
    <WasmShellRuntimeOptions>--jiterpreter-stats-enable --jiterpreter-estimate-heat</WasmShellRuntimeOptions>
</PropertyGroup>
```

Finally, runtime statistics are maintained by the jiterpreter and can be displayed by running `INTERNAL.jiterpreter_dump_stats()` in the browser debugger console.

## Mixed Interpreter and AOT Mode

This mode enables AOT compilation for most of the assemblies, with [some specific exceptions](https://github.com/dotnet/runtime/issues/50609).

By default, this mode is only enabled when running `dotnet publish`.

To enable AOT compilation on normal builds, use the following:

```xml
<PropertyGroup>
  <RunAOTCompilationAfterBuild>true</RunAOTCompilationAfterBuild>
</PropertyGroup>
```

### Required configuration for Mixed AOT Mode or static linking on Linux

- Ubuntu 20.04+ or a [container](https://hub.docker.com/r/unoplatform/wasm-build)
- A [.NET SDK](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu) >= 6.0

## Profile Guided AOT

This mode allows for the AOT engine to selectively optimize methods to WebAssembly, and keep the rest as interpreted. This gives a very good balance when choosing between performance and payload size. It also has the advantage of reducing the build time, as less code needs to be compiled down to WebAssembly.

This feature is used in two passes:

- The first pass needs the creation of a profiled interpreted build, which records any methods invoked during the profiling session.
- The second pass rebuilds the application using the Mixed AOT/Interpreter mode augmented by the recording created during the first pass.

This mode gives very good results, where the RayTracer sample of this repository goes from an uncompressed size of 5.5MB to 2.9MB.

To create a profiled build:

- In your Wasm csproj, add the following:

  ```xml
  <WasmShellGenerateAOTProfile>true</WasmShellGenerateAOTProfile>
  ```

- Run the application once, without the debugger (e.g. Ctrl+F5)
- Navigate throughout the application in high-usage places.
- Once done, either:
  - Press the `Alt+Shift+P` key sequence
  - Launch App.saveProfile()
- Download the `aot.profile` file next to the csproj file
- Comment the `WasmShellGenerateAOTProfile` line
- Add the following lines:

  ```xml
  <ItemGroup>
    <WasmShellEnableAotProfile Include="aot.profile" />
  </ItemGroup>
  ```

- Make sure that Mixed mode is enabled:

  ```xml
  <WasmShellMonoRuntimeExecutionMode>InterpreterAndAOT</WasmShellMonoRuntimeExecutionMode>
  ```

- Build you application again

Note that the AOT profile is a snapshot of the current set of assemblies and methods in your application. If that set changes significantly, you'll need to re-create the AOT profile to get optimal results.

### AOT Profile method exclusion

The generated profile contains all the methods found to be executed during the profiling session, but some methods may still need to be manually excluded for some reasons (e.g. runtime or compile time errors).

The `WasmShellAOTProfileExcludedMethods` property specifies a semi-colon separated list of regular expressions to exclude methods from the profile:

```xml
<PropertyGroup>
    <WasmShellAOTProfileExcludedMethods>Class1\.Method1;Class2\.OtherMethod</WasmShellAOTProfileExcludedMethods>

    <!-- use this syntax to separate the list on multiple lines -->
    <WasmShellAOTProfileExcludedMethods>$(WasmShellAOTProfileExcludedMethods);Class3.*</WasmShellAOTProfileExcludedMethods>
</PropertyGroup>
```

The `MixedModeExcludedAssembly` is also used to filter the profile for assemblies, see below for more information.

Dumping the whole list of the original and filtered lists is possible by adding:

```xml
<PropertyGroup>
    <WasmShellGenerateAOTProfileDebugList>true</WasmShellGenerateAOTProfileDebugList>
</PropertyGroup>
```

This will generate files named `AOTProfileDump.*.txt` in the `obj` folder for inspection.

### Mixed AOT/Interpreter Mode

This mode allows for the WebAssembly generation of parts of the referenced assemblies and falls back to the interpreter for code that was excluded or not known at build time.

This allows for a fine balance between payload size and execution performance.

At this time, it is only possible to exclude assemblies from being compiled to WebAssembly through the use of this item group:

```xml
<ItemGroup>
  <MonoRuntimeMixedModeExcludedAssembly Include="Newtonsoft.Json" />
</ItemGroup>
```

Adding assemblies to this list will exclude them from being compiled to WebAssembly.

### Troubleshooting Mixed AOT/Interpreter Mode

When using the Mixed AOT/Interpreter mode, it is possible that some methods may not be compiled to WebAssembly for a variety of reasons. This can cause performance issues, as the interpreter is slower than the AOT-generated code.

In order to determine which methods are still using the interpreter, you can use the following property:

```xml
<PropertyGroup>
  <WasmShellPrintAOTSkippedMethods>true</WasmShellPrintAOTSkippedMethods>
</PropertyGroup>
```

The logs from the AOT compiler can be found in [binlogs generated](https://aka.platform.uno/msbuild-troubleshoot) from the build.

### Increasing the Initial Memory Size

When building with Mixed AOT/Interpreter modes, the initial memory may need to be adjusted in the project configuration if the following error message appears:

```text
wasm-ld: error: initial memory too small, 17999248 bytes needed
```

In order to fix this, you'll need to set the [`INITIAL_MEMORY`](https://emscripten.org/docs/tools_reference/settings_reference.html?highlight=initial_memory#initial-memory) emscripten parameter, this way:

```xml
<ItemGroup>
  <WasmShellExtraEmccFlags Include="-s INITIAL_MEMORY=64MB" />
</ItemGroup>
```

which will set the initial memory size accordingly. Note that setting this value to a sufficiently large value (based on your app's usual memory consumption) can improve the startup performance.
