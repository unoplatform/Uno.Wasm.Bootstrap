---
uid: Uno.Wasm.Bootstrap.Runtime.Execution
---
## Runtime Execution Modes
The mono for WebAssembly runtime provides three execution modes, Interpreter, AOT (Ahead of Time) and Mixed Mode Interpreter/AOT.

The execution mode can be set as follows:
```xml
<WasmShellMonoRuntimeExecutionMode>Interpreter</WasmShellMonoRuntimeExecutionMode>
```
The possible values are:
- `Interpreter` (the default mode)
- `InterpreterAndAOT`

To setup your machine to use AOT modes on Windows, you will need to install [Python from Windows Store](https://www.microsoft.com/store/productId/9P7QFQMJRFP7), or manually through [Python's official site](https://www.python.org/downloads/).

### Interpreter mode
This mode is the slowest, but allows for great flexibility and debugging, as well as an efficient payload size. 

The linker mode can also be completely disabled for troubleshooting, as this will not impact the wasm payload size.

### Mixed Interpreter and AOT Mode
This mode enable AOT compilation for most of the assemblies, with [some specific exceptions](https://github.com/dotnet/runtime/issues/50609). 

This mode is generally prefered to FullAOT as it allows to load arbitrary assemblies and execute their code through the interpreter.

> [!IMPORTANT]
> These modes are not supported on macOS as of .NET 6 (Bootstrapper 3.x), and .NET 7 (Bootstrapper 4.x). You'll need to use a [Linux container](https://hub.docker.com/r/unoplatform/wasm-build) to build with AOT, see below for more details.

## Required configuration for AOT, Mixed Mode or static linking on Linux
- Ubuntu 18.04+ or a [container](https://hub.docker.com/r/unoplatform/wasm-build)
- A [stable build of mono](https://www.mono-project.com/download/stable/#download-lin)
- A [.NET SDK](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu) >= 5.0
- ninja: `apt-get install ninja-build`

The easiest is to build using the environment provided by the [unoplatform/wasm-build docker image](https://hub.docker.com/r/unoplatform/wasm-build).

## Profile Guided AOT
This mode allows for the AOT engine to selectively optimize methods to WebAssembly, and keep the rest as interpreted. This gives a very good balance when choosing between performance and payload size. It also has the advantage of reducing the build time, as less code needs to be compiled down to WebAssembly.

This feature is used in two passes:
- The first pass needs the creation of a profiled interpreter build, which records any methods invoked during the profiling session.
- The second pass rebuilds the application using the Mixed AOT/Interpreter mode augmented by the recording created during the first pass.

This mode gives very good results, where the RayTracer sample of this repository goes from an uncomressed size of 5.5MB to 2.9MB.

To create a profiled build:
- In your Wasm csproj, add the following: 
```xml
<WasmShellGenerateAOTProfile>true</WasmShellGenerateAOTProfile>
```
- In your `LinkerConfig.xml` file, add the following:
```xml
<assembly fullname="WebAssembly.Bindings" />
```
- Run the application once, without the debugger (e.g. Ctrl+F5)
- Navigate throughout the application in high usage places.
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

Dumping the whole list of original and filtered list is possible by adding:
```xml
<PropertyGroup>
    <WasmShellGenerateAOTProfileDebugList>true</WasmShellGenerateAOTProfileDebugList>
</PropertyGroup>
```
This will generate files named `AOTProfileDump.*.txt` in the `obj` folder for inspection.

### Mixed AOT/Interpreter Mode
This modes allows for the WebAssembly generation of parts of the referenced assemblies, and falls back to the interpreter for code that was excluded or not known at build time.

This allows for a fine balance between payload size and execution performance.

At this time, it is only possible to exclude assemblies from being compiled to WebAssembly through the use of this item group:

```xml
<ItemGroup>
  <MonoRuntimeMixedModeExcludedAssembly Include="Newtonsoft.Json" />
</ItemGroup>
```
Adding assemblies to this list will exclude them from being compiled to WebAssembly.

## Required configuration for AOT, Mixed Mode or external bitcode support compilation on Windows 10

### Native windows tooling
This is the default mode on Windows. It requires installing [Python from Windows Store](https://www.microsoft.com/store/productId/9P7QFQMJRFP7), or manually through [Python's official site](https://www.python.org/downloads/).

This mode is compatible with CI servers which have Python installed by default, such as [Azure Devops Hosted Agents](https://docs.microsoft.com/en-us/azure/devops/pipelines/agents/hosted?view=azure-devops).

#### Powershell setup
The bootstrapper needs to use powershell, and configuration is needed.

You'll need to run the following command in an elevated (administrator) PowerShell prompt:
```
Set-ExecutionPolicy RemoteSigned -Force
```

You may also need to enable the developer mode for Windows 10 and 11 by using **Control panel** / **System** / **Privacy & Security** / **For developers** / **PowerShell** and setting **Change execution policy to allow local scripts to run without signing** to **On**.

### Using Windows Subsystem for Linux
This mode can be enabled by adding this property to the `csproj`:
```xml
<PropertyGroup>
  <WasmShellEnableEmscriptenWindows>false</WasmShellEnableEmscriptenWindows>
</PropertyGroup>
```

Requirements:
- A Windows 10 machine with [WSL 1 or 2 with Ubuntu 20.04](https://docs.microsoft.com/en-us/windows/wsl/install-win10) installed.
- A [stable build of mono](https://www.mono-project.com/download/stable/#download-lin)
- A [.NET SDK](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu) >= 3.1
- ninja: `apt-get install ninja-build` in WSL

If you have another distribution installed make sure that the 20.04 is the default using `wslconfig /s "Ubuntu-20.04"`. You can list your active distributions with `wslconfig /l`
Note that WSL 2 is considerably slower than WSL 1 for the boostrapper scenario. You will need to set your distribution to version 1 using `wsl --set-version "Ubuntu-20.04" 1`.

During the first use of WSL, if the environment is not properly setup, you will be guided to run the [`dotnet-setup.sh`](/src/Uno.Wasm.Bootstrap/build/scripts/dotnet-setup.sh) script that will install Mono, .NET Core and some additional dependencies.

The emscripten installation is automatically done as part of the build.

The boostrapper uses its own installation of emscripten, installed by default in `$HOME/.uno/emsdk` in the WSL filesystem. This can be globally overriden by setting the `WASMSHELL_WSLEMSDK` environment variable.

### WSL Integration for Windows 10

The integration with WSL provides a way for using AOT, Mixed mode or external bitcode support using Windows 10.

This feature is active only if one of those condition is true:
- The `WasmShellMonoRuntimeExecutionMode` property is `FullAOT` or `InterpreterAndAOT
- There is a `*.bc` or `*.a` file in the `Content` item group
- The `WasmShellForceUseWSL` is set to `true`

