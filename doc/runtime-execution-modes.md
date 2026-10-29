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

This mode is only active when running `dotnet publish`, `dotnet build -r Release` or any other build mode will not activate it.

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
- Add a reference to the generated file:
  - If you're using the [Uno.Sdk](xref:Uno.Features.Uno.Sdk), place the file in the `Platforms/WebAssembly` folder
  - If you're not using the Uno.Sdk, add the following lines:

    ```xml
    <ItemGroup>
      <WasmShellEnableAotProfile Include="aot.profile" />
    </ItemGroup>
    ```

- Make sure that Mixed mode is enabled:

  ```xml
  <WasmShellMonoRuntimeExecutionMode>InterpreterAndAOT</WasmShellMonoRuntimeExecutionMode>
  ```

- Publish your application again, using `dotnet publish`. (`dotnet build` does not activate AOT)

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

The `MixedModeExcludedAssembly` is also used to filter the profile for assemblies, see [Mixed AOT/Interpreter Mode](#mixed-aotinterpreter-mode) for more information.

Dumping the whole list of the original and filtered lists is possible by adding:

```xml
<PropertyGroup>
    <WasmShellGenerateAOTProfileDebugList>true</WasmShellGenerateAOTProfileDebugList>
</PropertyGroup>
```

This will generate files named `AOTProfileDump.*.txt` in the `obj` folder for inspection.

### Preventing crashes in profile-guided AOT builds

An app published with an AOT profile can crash at run time when its compiled code calls into code that the profile doesn't include: code that didn't run while the profile was recorded, or code added since. The app stops responding, and the browser console shows:

```text
[MONO] * Assertion at .../src/mono/mono/mini/interp/interp.c:3420, condition `<disabled>' not met
```

To prevent this, the bootstrapper adds the small amount of extra compiled code that these calls need, then checks that it was generated. It's on by default in apps published with an AOT profile (see above). It adds a few seconds to the build of a large app, which a publish without changes skips. It makes `dotnet.native.wasm` about 0.5% to 0.7% larger, and less than that once compressed.

The build can report:

| Code | Description |
|------|-------------|
| UNOWASM006 | Error, which fails the build: some of the extra code is missing, so the app may still crash when it reaches the method the message names. Please [open an issue](https://github.com/unoplatform/Uno.Wasm.Bootstrap/issues) with the message and the files described below. Meanwhile, you can set `WasmShellEnableAOTWrapperSeedingCheck` to `false` to turn off the check (see below): the extra code that was generated is still added. |
| UNOWASM007 | Error, which fails the build: the check couldn't read the compiled code; the message says why. Please open an issue with the message. Meanwhile, you can set `WasmShellEnableAOTWrapperSeedingCheck` to `false` to turn off the check. |
| UNOWASM008 | Warning: the extra code couldn't be generated, or the .NET SDK skipped the build steps that add it, so the build continues without it, and the crash can still happen. Please open an issue with the message and a binary log of the publish (see below). |
| UNOWASM009 | Warning: the extra code needs AOT deduplication, which is off because `WasmShellEnableAOTDeduplication` or `WasmDedup` is `false`, so the build continues without it, and the crash can still happen. Turn deduplication back on (it's on by default), or turn off the crash prevention to remove the warning. |
| UNOWASM010 | Warning: compiled code that calls the method the message names can crash the app, and the extra code can't prevent it, because of how one of its parameters is declared. See [UNOWASM010](#unowasm010) below. |
| UNOWASM011 | Warning: some generic code is too large, or makes generic types without end, as recursive generic code can, so the extra code stops following it, and the crash can still happen in the code the message names. Run that code while you record the AOT profile (see above), so that it's compiled. Please open an issue with the message. To remove the warning, add `UNOWASM011` to `NoWarn`. |

The build only reports UNOWASM010 and UNOWASM011 when it generates the extra code, which it does again when the app's code or its packages change: a publish without changes doesn't repeat them.

To turn off the crash prevention, or only its check:

```xml
<PropertyGroup>
    <!-- Turns off the crash prevention and its check -->
    <WasmShellEnableAOTWrapperSeeding>false</WasmShellEnableAOTWrapperSeeding>

    <!-- Keeps the crash prevention, turns off its check -->
    <WasmShellEnableAOTWrapperSeedingCheck>false</WasmShellEnableAOTWrapperSeedingCheck>
</PropertyGroup>
```

The first publish after the crash prevention stops applying compiles the generic code that compiled methods share again, without the extra code.

The extra code covers the interfaces, virtual methods and delegates of the app's code and of the generic code it uses, including generic `async` methods, iterators, lambdas, static field initializers, the constructors that `new T()` runs, static abstract interface members, overrides and interface implementations in generic classes, and the implementations of generic methods (methods with type parameters of their own) in classes and structs that aren't generic nor nested in generic ones. Code that only appears at run time is partly covered: for classes created through reflection, as dependency injection containers do, and for generic types created with `MakeGenericType`, only the interfaces that compiled code calls are covered; delegates created with `Delegate.CreateDelegate` aren't. Neither is the code of generic classes and structs, and of the classes and structs nested in them, that implements generic methods (overrides of generic virtual methods, implementations of generic interface methods and of generic static abstract members), whether code calls them or creates delegates to them, nor methods whose parameters include a `TypedReference`, `ArgIterator` or `RuntimeArgumentHandle`.

When you report a problem, attach the files of the `obj/<configuration>/<target framework>/aot-wrapper-seeding` folder to the issue, and a [binary log](https://aka.platform.uno/msbuild-troubleshoot) of the publish if you can.

#### UNOWASM010

The warning names a method one of whose parameters is a struct with an explicit layout or many fields, such as `Matrix4x4`, or a function pointer, and has a default value, or an `[Optional]`, `[In]`, `[Out]` or `[MarshalAs]` attribute. Other structs, such as `CancellationToken` or `Guid`, aren't affected. Unlike them, the runtime passes such a struct as it is between compiled and interpreted code, along with what the default value or attribute adds to the parameter, and then can't find the code that does it. So compiled code that calls the method crashes the app when it makes the call, or, if the method has more than 8 parameters, as soon as that code starts running. If the method implements an interface method and has more than 8 parameters, compiled code that calls another method of its class through the interface crashes too. Listing the method in the AOT profile doesn't help: the app then crashes when interpreted code calls it instead. Remove the default value or the attribute, or pass the parameter with `in`. If the method is in a library you can't change, report the warning to the library's authors.

The same warning names, once each, the structs that contain themselves through a field such as a `ReadOnlyMemory<T>` or an `ArraySegment<T>` of their own type, as `struct Node { ReadOnlyMemory<Node> Children; }` does, and the generic structs with more than 15 type arguments: the methods that take or return them, or a struct that holds them, by value have the same problem. Pass such structs with `in`, hold their items in a class or an array, or give them at most 15 type arguments. Recording the code that uses them in the AOT profile doesn't help either: the AOT compiler can fail on it, which fails the build.

To remove the warning, add `UNOWASM010` to `NoWarn`.

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
