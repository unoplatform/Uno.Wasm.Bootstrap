# Uno.Wasm.Bootstrap

[![Open in Gitpod](https://gitpod.io/button/open-in-gitpod.svg)](https://gitpod.io/#https://github.com/unoplatform/Uno.Wasm.Bootstrap) 

Uno.Wasm.Bootstrap provides a simple way to package C# .NET code, and run it from a compatible browser environment.

It is a standalone .NET Web Assembly (WASM) sdk bootstrapper taking the form of a nuget package.

Installing it on a .NET 5 project or .NET Standard 2.0 library with an entry point allows to publish it as part of a WASM distribution folder, along with CSS, Javascript and content files.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`.

This package is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

## How to use the package with .NET 5 and later
* Create a .NET 5 Console Application, and update it with the following basic definition:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net5.0</TargetFramework>
    <MonoRuntimeDebuggerEnabled Condition="'$(Configuration)'=='Debug'">true</MonoRuntimeDebuggerEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Uno.Wasm.Bootstrap" Version="2.1.0" />
    <PackageReference Include="Uno.Wasm.Bootstrap.DevServer" Version="2.1.0" PrivateAssets="all" />
 </ItemGroup>

</Project>
```

* Add a main entry point:
```csharp
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello from C#!");
    }
}
```

* In Visual Studio 2019, press `Ctrl+F5` to start without the debugger (this will create the `launchSettings.json` needed below for debugging)
* A browser window will appear with your application
* The output of the Console.WriteLine will appear in the javascript debugging console

### How to use the Visual Studio 2019 Debugger
Starting from **Visual Studio 2019 16.6**, it is possible to debug a WebAssembly app.

To enable the debugging, add the following line to your `launchSettings.json` file:
```json
"inspectUri": "{wsProtocol}://{url.hostname}:{url.port}/_framework/debug/ws-proxy?browser={browserInspectUri}"
```

in every profile section of the file, below each `"launchBrowser": true,` line.

Press `F5` to start debugging.

### Alternate deployment methods
Install the `[dotnet serve](https://github.com/natemcmaster/dotnet-serve)` tool:
```
dotnet tool install -g dotnet-serve
```
Once installed, launch the server by using the following command:
```bash
cd MyApp.Wasm
dotnet serve -d bin\Debug\net5.0\dist -p 8000
```
You application will be available `http://localhost:8000`.

### Upgrading from previous versions of the Uno.Wasm.Bootstrap package
Previously, the suggested project structure was a .NET Standard 2.0 project using the non-web projects SDK. To enable debugging and easier deployment, the support for `Microsoft.NET.Sdk.Web` has been added.

To upgrade a project from 1.1 to 1.2:
- If you had a `<DotNetCliToolReference />` line, remove it
- Add the `<PackageReference Include="Uno.Wasm.Bootstrap.DevServer" Version="1.2.0-dev.1" PrivateAssets="all" />` item in the same item group as the other nuget packages.

To upgrade a project from 1.0 to 1.1:
- Change `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web` in the Sdk attribute of your project
- Add the `<DotNetCliToolReference Include="Uno.Wasm.Bootstrap.Cli" Version="1.0.0-dev.1" />` item in the same item group as the other nuget packages.

## Linker configuration
The .NET tooling uses the [ILLinker](https://github.com/mono/linker/tree/master/), and can be configured using a linker directives file.

The Bootstrapper searches for a file placed in an ItemGroup named `LinkerDescriptor`. See examples below.

### Configuration file (commonly named `LinkerConfig.xml`)

```xml
<linker>
    <assembly fullname="Uno.Wasm.Sample"> <!-- Replace names to reflect your needs -->
        <namespace fullname="Uno.Wasm.Sample" />
    </assembly>

    <assembly fullname="WebAssembly.Bindings" />
</linker>
```

The documentation for this file [can be found here](https://github.com/mono/linker/blob/master/docs/data-formats.md#xml-examples).

### Reference in project file

It is also possible to provide the linker file as an embedded resource, which is useful when creating libraries. The linker step will discover those files and apply the configuration.

``` xml
<!-- For application projects, include this in the .csproj file of your Wasm project -->
<ItemGroup>
    <LinkerDescriptor Include="LinkerConfig.xml" />
</ItemGroup>

<!-- For libraries, you should use this syntax instead -->
<ItemGroup Condition="'$(TargetFramework)' == 'net5.0'">
	<EmbeddedResource Include="LinkerConfig.xml">
		<LogicalName>$(AssemblyName).xml</LogicalName>
	</EmbeddedResource>
</ItemGroup>
```

The Linker can be disabled completely by setting the `WasmShellILLinkerEnabled` property to false. This property has no effect when building with AOT enabled.

### .NET 5 Feature Linker Configuration
The bootstrapper supports the [feature switches configuration](https://github.com/dotnet/runtime/blob/master/docs/workflow/trimming/feature-switches.md) provided by .NET 5.

By default, some features are linked out as those are not likely to be used in a WebAssembly context:
- `EventSourceSupport`
- `EnableUnsafeUTF7Encoding`
- `HttpActivityPropagationSupport`

If you need to enable any of those features, you can set the following in your csproj first `PropertyGroup`:
```xml
<EventSourceSupport>true</EventSourceSupport>
```

## Publishing the build results
The easiest way to publish the build results is to use the Visual Studio publish menu on your project. This will allow to use all the features provided by the standard experience, as described in the [Deploy to Azure App Service](https://docs.microsoft.com/en-us/visualstudio/deployment/quickstart-deploy-to-azure?view=vs-2017).

The publication of the application must be done in .NET Framework hosting (and not .NET Core), as the app uses the `web.config` file for the server configuration, and to enable the use of pre-compression.

For deeper integration in the publishing pipeline, the `WasmShellOutputPackagePath` property is defined by the bootstrapper after the `BuildDist` target, which contains the path to the generated `package_XXX` content.

## Serve the Wasm app through Windows Linux Subsystem
Using Windows 10, serving the app through a small Web Server is done through WSL.

Here's how to install it:
- Search for Ubuntu in the Windows Store: https://www.microsoft.com/en-us/search/result.aspx?q=ubuntu
- Install Ubuntu 18.04 or later, and follow the instructions during the first run
	- If you have another distribution installed make sure that the 18.04 is the default using `wslconfig /s "Ubuntu-20.04"`. You can list your active distributions with `wslconfig /l`
	- Note that WSL 2 is considerably slower than WSL 1 for the boostrapper scenario. You will need to set your distribution to version 1 using `wsl --set-version "Ubuntu-20.04" 1`.
- Once you've built your project, you should see a path to the project dll
- In the Ubuntu shell, type ``cd `wslpath "[the_path_to_your_bin_folder]\dist"` ``
- Type `python3 server.py`
    - If this command does not exist, run the following `sudo apt-get install python3`
- Using your favorite browser, navigate to `http://localhost:8000`

## .NET for WebAssembly Debugger Support

Debugging is supported through the integration of a .NET Core CLI component, which acts as a static files server, as well as a debugger proxy for Chrome (other browsers are not supported).

### Enable the Debugger support

In order to debug an **Uno.Wasm.Boostrap** enabled project, the Mono runtime debugger must be enabled:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
   <MonoRuntimeDebuggerEnabled>true</MonoRuntimeDebuggerEnabled>
</PropertyGroup>
```

Debug symbols need to be emitted and be of the type `portable`:

```xml
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
    <DebugType>portable</DebugType>
    <DebugSymbols>true</DebugSymbols>
</PropertyGroup>
```

Finally, the `DEBUG` constant must be defined

```xml
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
    <DefineConstants>$(DefineConstants);TRACE;DEBUG</DefineConstants>
</PropertyGroup>
```

Doing so will enable the deployment of `pdb` files to the browser, and allow for the mono debugger proxy to use them. 

For the time being, you will also need to make sure that mscorlib is disabled in the Linker configuration file: 

```xml
<!-- Required for debugging -->
<assembly fullname="mscorlib" />
<assembly fullname="System.Core" />
```

.NET for WebAssembly now has integrated **preliminary** support for in-browser debugging. Refer to
[this document for up-to-date information](https://github.com/mono/mono/tree/master/sdks/wasm#debugging) on how to set up the debugging.

### How to use the Browser debugger
The boostrapper also supports debugging directly in the browser debugging tools.

In Visual Studio:
- Make your project the startup project (right-click **set as startup**)
- In the debugging toolbar:
  - Select **IIS Express** as the debugging target
  - Select **Chrome** as the Web Browser
  - Make sure script debugging is disabled
- Start the debugging session using F5 (or Start Debug)
- Once your application has started, press `Alt+Shift+D`
- Follow the instructions on the web page
- You may need to refresh the original tab if you want to debug the entry point (Main) of your application.

### Debugger troubleshooting
The debugger is still under development, and here are a few things to look for:
- Breakpoints set sometimes disappear when the debugged page is reloaded
- If none of your assemblies appear in the debugger window, it's generally caused 
by the debugger caching previously loaded files. Make sure to hit Ctrl+Shit+R to force 
reload the debugged page.

## Runtime Execution Modes
The mono for WebAssembly runtime provides three execution modes, Interpreter, AOT (Ahead of Time) and Mixed Mode Interpreter/AOT.

The execution mode can be set as follows:
```xml
<WasmShellMonoRuntimeExecutionMode>Interpreter</WasmShellMonoRuntimeExecutionMode>
```
The possible values are:
- `Interpreter` (the default mode)
- `InterpreterAndAOT`
- `FullAOT`

To setup your machine to use AOT modes on Windows, you will need to install [Python from Windows Store](https://www.microsoft.com/store/productId/9P7QFQMJRFP7), or manually through [Python's official site](https://www.python.org/downloads/).

### Interpreter mode
This mode is the slowest of all three, but allows for great flexibility and debugging, as well as an efficient payload size. 

The linker mode can also be completely disabled for troubleshooting, as this will not impact the wasm payload size.

### Full AOT Mode
This mode generates WebAssembly binary for all the referenced assemblies and provides the fastest code execution, but also generates the largest payload. This mode will not allow the execution of code that was not known at compile time (e.g. dynamically generated assemblies or loaded through `Assembly.LoadFrom`).

> The FullAOT mode currently fails at runtime using net5 or net6 because of [this issue](https://github.com/dotnet/runtime/issues/50609).

### Mixed Interpreter and AOT Mode
This mode enable AOT compilation for most of the assemblies, with [some specific exceptions](https://github.com/dotnet/runtime/issues/50609). 

This mode is generally prefered to FullAOT as it allows to load arbitrary assemblies and execute their code through the interpreter.

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

### AOT Debugging and mono tracing (.NET 5 only)

When running with PG-AOT/FullAOT, exceptions generally do not provide stack traces, as WebAssembly as of the MVP does not yet support stack walking.

For the time being, it's still possible view browser stack traces in the log by enabling mono tracing.

First, you'll need to add the following class to your app:

```csharp
static class MonoInternals
{
	[DllImport("__Native")]
	internal static extern void mono_trace_enable(int enable);
	[DllImport("__Native")]
	internal static extern int mono_trace_set_options(string options);
}
```

Then in the `Main` of your application, add the following:
```csharp
MonoInternals.mono_trace_enable(1);
MonoInternals.mono_trace_set_options("E:all");
```

This will enable the tracing of all application exceptions (caught or not), along with the associated native host stack traces.

You can find the documentation for [`mono_trace_set_options` parameter here](https://www.mono-project.com/docs/debug+profile/debug/#tracing-program-execution).

## Required configuration for static linking on macOS
- macOS 10.15 or later
- [Homebrew](https://brew.sh)
- [.NET SDK 5.0](https://dotnet.microsoft.com/download/dotnet/5.0) for net5 builds
- A recent build of [Mono MDK - Stable](https://www.mono-project.com/download/stable/#download-mac)
- ninja: `brew install ninja`

We recommend that you install newer versions of Uno.Wasm.Bootstrap as it provides a better out-of-the-box experience(e.g. Emscripten is automatically installed).

Please note that `AOT` and `Mixed Mode` are not supported yet.

## Required configuration for AOT, Mixed Mode or static linking on Linux
- Ubuntu 18.04+ or a [container](https://hub.docker.com/r/unoplatform/wasm-build)
- A [stable build of mono](https://www.mono-project.com/download/stable/#download-lin)
- A [.NET SDK](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu) >= 3.1
- ninja: `apt-get install ninja-build`

We recommend that you install newer versions of Uno.Wasm.Bootstrap as it provides a better out-of-the-box experience(e.g. Emscripten is automatically installed).

The easiest is to build using the environment provided by the [unoplatform/wasm-build docker image](https://hub.docker.com/r/unoplatform/wasm-build).

## Required configuration for AOT, Mixed Mode or external bitcode support compilation on Windows 10

### Native windows tooling
This is the default mode on Windows. It requires installing [Python from Windows Store](https://www.microsoft.com/store/productId/9P7QFQMJRFP7), or manually through [Python's official site](https://www.python.org/downloads/).

This mode is compatible with CI servers which have Python installed by default, such as [Azure Devops Hosted Agents](https://docs.microsoft.com/en-us/azure/devops/pipelines/agents/hosted?view=azure-devops).

### Using Windows Subsystem for Linux
This mode can be enabled by adding this property to the `csproj`:
```xml
<PropertyGroup>
  <WasmShellEnableEmscriptenWindows>false</WasmShellEnableEmscriptenWindows>
</PropertyGroup>
```

Requirements:
- A Windows 10 machine with [WSL 1 or 2 with Ubuntu 18.04](https://docs.microsoft.com/en-us/windows/wsl/install-win10) installed.
- A [stable build of mono](https://www.mono-project.com/download/stable/#download-lin)
- A [.NET SDK](https://docs.microsoft.com/en-us/dotnet/core/install/linux-ubuntu) >= 3.1
- ninja: `apt-get install ninja-build` in WSL

If you have another distribution installed make sure that the 18.04 is the default using `wslconfig /s "Ubuntu-20.04"`. You can list your active distributions with `wslconfig /l`
Note that WSL 2 is considerably slower than WSL 1 for the boostrapper scenario. You will need to set your distribution to version 1 using `wsl --set-version "Ubuntu-20.04" 1`.

During the first use of WSL, if the environment is not properly setup, you will be guided to run the [`dotnet-setup.sh`](/src/Uno.Wasm.Bootstrap/build/scripts/dotnet-setup.sh) script that will install Mono, .NET Core and some additional dependencies.

The emscripten installation is automatically done as part of the build.

The boostrapper uses its own installation of emscripten, installed by default in `$HOME/.uno/emsdk` in the WSL filesystem. This can be globally overriden by setting the `WASMSHELL_WSLEMSDK` environment variable.

#### Special considerations for CI servers (GitHub Actions, Azure Devops)
When building an application on Windows based CI servers, WSL is generally not enabled in base images. This can cause builds to fail if they require the use of static linking and/or AOT.

In order to work around this issue, the following property can be set:
```xml
<WasmShellForceDisableWSL>true</WasmShellForceDisableWSL>
```

**It is important to note that generating a build this way, on a Windows CI server, without WSL enabled will generate an interpreter only build, and generate an invalid package if static linking was to be required.**

When using GitHub actions:
```xml
<WasmShellForceDisableWSL Condition="'$(CI)'=='true'">true</WasmShellForceDisableWSL>
```
When using Azure Devops:
```xml
<WasmShellForceDisableWSL Condition="'$(BUILD_BUILDID)'=='true'">true</WasmShellForceDisableWSL>
```

## Debugging and contributing to the Uno WebAssembly Bootstrapper

The [src/Uno.Wasm.Bootstrap.sln](src/Uno.Wasm.Bootstrap.sln) solution is a good way to build the bootstrapper itself, as well as sample solutions that validate the different features of the bootstrapper.

### Debugging in Visual Studio for Windows
- Select a sample application, such as the `Uno.Wasm.Sample` project, and press `Ctrl+F5` or run **without debugger**. 
- The bootstrapper will be built as part of the process, and will generate a new webassembly site layout.
- Once the application has built, it will run in the selected browser in the Visual Studio debug location toolbar

Some tips:
- If you make modifications to the `Uno.Wasm.Bootstrap`, you may have to terminate all `msbuild.exe` processes, as they may lock files of that project.
- If you make modifications to the `Uno.Wasm.Bootstrap.Cli` project, you may have to terminate the `dotnet.exe` processes that link to your solution's subfolders, as they may lock files of that project.

Once the processes have been terminated, restart your build.

Debugging the bootstrapper task can be done by adding a `Debugger.Launch()` statement in the `Run` method of `ShellTask.cs`.

### Testing the bootstrapper through GitPod 
You can also make contributions through GitPod, and validate that your changes are appropriate.

Building and debugging samples is done through the command line.
1. Build a sample using :
   ```
   cd src/Uno.Wasm.Sample
   msbuild /r /bl
   ```
1. Start the web server to serve the sample on port 8000:
   ```
   cd bin/Debug/net5.0/dist
   python3 server.py
   ```
1. The GitPod IDE will open a preview window with the content of the site. You may need to open the browser debugger window to see the results of the sample's execution.

Click on the button below to try this out!

[![Open in Gitpod](https://gitpod.io/button/open-in-gitpod.svg)](https://gitpod.io/#https://github.com/unoplatform/Uno.Wasm.Bootstrap) 

## Features
### WebAssembly Module Linking support

#### Static Linking overview
Statically linking Emscripten LLVM Bitcode (`.bc` and `.a` files) files to mono is supported on both Windows 10 and Linux. To build on Windows please refer to the AOT environment setup instructions.

This linking type embeds the `.bc` or `.a` files with the rest of the WebAssembly modules, and uses _normal_ webassembly function invocations that are faster than with dynamic linking.

Any `.bc` or `.a` file placed as `content` in the built project will be statically linked to the currently running application.

This allowing for p/invoke to be functional when resolving methods from the loaded module. If you have a `.bc` or a `.a` file you don't want to be include in the linking, you may add the `UnoAotCompile="false"` metadata that way:

``` xml
<ItemGroup>
    <!-- Deactivate the discovery of a .bc or a .a file for static linking -->
    <Content Update="path\to\my\file.bc" UnoAotCompile="False" />
</ItemGroup>
```

#### Static Linking multi-version support
As emscripten's ABI is not guaranteed to be compatible between versions, it may also be required to include multiple versions of the same LLVM binaries, compiled against different versions of LLVM.
In order to enable this scenario, the Uno Bootstrapper supports adding .bc files by convention.

If the bitcode file to be added is named `libTest.bc`, the following structure can be used in your project:
- `libTest.bc/2.0.6/libTest.bc`
- `libTest.bc/2.0.9/libTest.bc`

In this case, based on the emscripten version used by the mono runtime, the bootstrapper will choose the closest matching version.

#### Static Linking additional emcc flags
Static linking may also require some additional emscripten flags, for instance when using libpng. In such a case, add the following to your project:

```xml
<ItemGroup>
	<WasmShellExtraEmccFlags Include="-s USE_LIBPNG=1"/>
</ItemGroup>
```

For more information, see the `Uno.Wasm.StaticLinking.Aot` sample side module build script.

### Static linking additional P/Invoke libraries

When building applications, in some cases, NuGet provided libraries may use native dependencies that are emscripten provided libraries, such as `libc`.

In such cases, the boostrapper allows for providing a set of known P/Invoke libraries as follows:

```xml
<ItemGroup>
    <WasmShellAdditionalPInvokeLibrary Include="libc" />
</ItemGroup>
```

It's important to note that providing additional libraries this way implies that all the imported functions will have to be available during emcc link operation.

Any missing function will result in a missing symbol error.

### Additional C/C++ files

The bootstrapper provides the ability to include additional C/C++ files to the final package generation. 

This feature can be used to include additional source files for native operations that may be more difficult to perform from managed C# code, but can also be used to override some weak aliases with [ASAN](https://emscripten.org/docs/debugging/Sanitizers.html).

#### Usage
```xml
<ItemGroup>
    <WasmShellNativeCompile Include="myfile.cpp" />
</ItemGroup>
```

The file is provided as-is to `emcc` and its resulting object file is linked with the rest of the compilation.

This feature is meant to be used for small additions of native code. If more is needed (e.g. adding header directories, defines, options, etc...) it is best to use the emcc tooling directly.

#### Example

Here's an example of file:
```cpp
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#define WASM_EXPORT __attribute__((visibility("default")))

extern "C" {
	WASM_EXPORT int additional_native_add(int a, int b);
}

WASM_EXPORT int additional_native_add(int a, int b) {
	printf("test_add(%d, %d)\r\n", a, b);
	return a + b;
}
```

which can be used in C# as follows:

```csharp
static class AdditionalImportTest
{
	[DllImport("__Native")]
	internal static extern int additional_native_add(int left, int right);
}
```

#### Emscripten Linker optimizations flags

When building with AOT or using static linking of bitcode files, the emscripten linker step is enabled and runs optimizations on the generated code.

These steps can be very expensive depending on the final binary size, and disabling those optimizations can be useful to improve the development loop.

To control those optimizations, use the following msbuild property:
```xml
<PropertyGroup>
    <WasmShellEmccLinkOptimization>false</WasmShellEmccLinkOptimization>
</PropertyGroup>
```

This flag is automatically set to `false` when running in a configuration named `Debug`.

The optimization level can be adjusted with the following:
```xml
<PropertyGroup>
    <WasmShellEmccLinkOptimizationLevel>Level3</WasmShellEmccLinkOptimizationLevel>
</PropertyGroup>
```

Allowed values are:
- `None` (`-O0`)
- `Level1` (`-O1`)
- `Level2` (`-O2`)
- `Level3` (`-O3`)
- `Maximum` (`-Oz`)
- Any other value will be passed onto emcripten without modifications

The default value is `Level3`.

#### Invoking emscripten and Mono/.NET 5 native functions

In order to invoke emscripten and mono native functions, the bootstrapper exposes the special library name `__Native`. 

For instance, the following enables the mono internal tracing:

```
static class MonoInternals
{
	[DllImport("__Native")]
	internal static extern void mono_trace_enable(int enable);
}
```

Or use emscripten functions:

```
[DllImport("__Internal_emscripten")]
public static extern void emscripten_console_log(string str);
```

### Threads support
> .NET 5/6 for WebAssembly does not support Threading (as of 2021-01-01), enabling this option will result in build errors (missing `dotnet.worker.js` file)

Mono now supports the ability to create threads, in browsers that support it (Chrome 79+, Edge 81+).  Threads are backed by [`atomics` and WebWorkers](https://emscripten.org/docs/porting/pthreads.html).

To enable the support, add the following configuration:

```xml
<MonoWasmRuntimeConfiguration>threads-release</MonoWasmRuntimeConfiguration>
```

Note that executing javascript in the context of a thread stays in the worked that is assigned to the thread, thus modifying the DOM from that context will do nothing.

To update the UI, execution will need to go back to the main thread, generally by using a mecanism similar to `System.Threading.Timer` which uses `setTimeout` so execute on the main thread.

### Pre-Compression support

Pre-compression has two modes: 
- In-place, where Brotli compressed files are placed next to original files
- Legacy, where a `web.config` file url rewriter rule is used

The parameters for the pre-compression are as follows:
- `WasmShellGenerateCompressedFiles` which can be `true` or `false`. This property is ignored when building `MonoRuntimeDebuggerEnabled` is set to `true`, and `true` by default when the `Configuration` property is set to `Release`
- `WasmShellCompressedExtension` is an item group which specifies which files to compress. By default `wasm`, `clr`, `js`, `css` and `html files are pre-compressed. More files can be added as follows:
```xml
  <ItemGroup>
    <WasmShellCompressedExtension Include=".db"/>
  </ItemGroup>
```
- `WasmShellBrotliCompressionQuality` which controls the compression quality used to pre-compress the files. The default value is 7.
- `WasmShellCompressionLayoutMode` which can be set to `InPlace` or `Legacy`. If not set and for backward compatility reasons, `Legacy` is automatically selected if a `web.config` file is detected in the layout, and contains the `_compressed_br` string.

#### Support for in-place compression

This mode is to be preferred for web servers that support `accept-encoding` header file rewriting. In the case of [**Azure Static WebApps**](https://docs.microsoft.com/en-us/azure/static-web-apps/get-started-portal), if a file next to the original one is suffixed with `.br`, and the client requested for brotli compressed files, the in-place compressed file will be served.

#### Support for IIS / Azure Webapp GZip/Brotli pre-compression
The IIS compression support has too many knobs for the size of generated WebAssembly files, which
makes the serving of static files inefficient.

The Bootstrapper tooling will generate two folders `_compressed_gz` and `_compressed_br` which contain compressed versions of the main files. A set IIS rewriting rules are used to redirect the queries to the requested pre-compressed files, with a preference for Brotli.

When building an application, place [the following file](src/Uno.Wasm.Sample/wwwroot/web.config) in the `wwwroot` folder to automatically enable the use of pre-compressed files.

Note that the pre-compressed files are optional, and if the rewriting rules are removed or not used (because the site is served without IIS), the original files are available at their normal locations.

### Node.js support

The bootstrapper supports having a project loaded as part of a node application. To do so:

- Create a Wasm bootstrapper project, named `MyApp.Wasm`
- Create a Node.js TypeScript project in Visual Studio, named `MyApp.Runner`
- In boostrapper project, add the following :
  ```xml
  <WasmShellDistPath>../MyApp.Runner/app</WasmShellDistPath>
  <WasmShellMode>node</WasmShellMode>
  ```
- In the `app.ts`, add the following:
  ```js
  require("./app/mono");
  ```

Run the application and the main method of the `MyApp.Wasm` will be executed.

The parameters of the node command line are provided to the app's main method when running the app as follows:

```
node app param1 param2
```

An example of the node.js support is available in the `Uno.Wasm.Node.Sample` and `Uno.Wasm.Node.Sample.Runner.njsproj` projects.

### Browser Embedded mode

By default, the project is launched with a HTML page (`index.html`). This mode is used for SPAs (Single Page Applications), but does not allow embedding into an existing webpage or application.

It is possible to use the Browser Embedded mode to allow the launching using JavaScript instead.

1. Add this line in your project file:

   ``` xml
   <WasmShellMode>BrowserEmbedded</WasmShellMode>
   ```

   The `embedded.js` file will be generated instead of `index.html`, containing the required code to launch the application.

2. In the HTML where you want to host the application, add the following:
   Using HTML:

   ``` html
   <div id="uno-body" />
   <script src="https://path.to/your/wasm/app/embedded.js" />
   ```

   Using a script:

   ``` javascript
   // you must ensure there's a <div id="uno-body" /> present in the DOM before calling this:
   import("https://path.to/your/wasm/app/embedded.js");
   ```

   

#### Important notes about Browser Embedded mode:

* There is no script isolation mechanisms, meaning that the application will have access to the same context and global objects.
* Loading more than one Uno bootstrapped application in the same page will conflict and produce unwanted results. A workaround would be to use a `<iframe>`.
* It may be required to add **CORS headers** to the application hosting website to allow the download and execution of scripts from it. It's already allowed in the development server shipped with Uno Bootstrapper.

### Support for additional JS files

Providing additional JS files is done through the inclusion of `EmbeddedResource`  msbuild item  files, in a project folder named `WasmScripts`.
Files are processed as embedded resources to allow for libraries to provide javascript files.

### Support for additional CSS files
Additional CSS files are supported through the inclusion of `EmbeddedResource`  msbuild item files, in a project folder named `WasmCSS`.

### Support for additional Content files
Additional Content files are supported through the inclusion of `Content` files. The folder structure is preserved in the output `dist` folder. There is 3 deployment modes for content files:

* `Package`: files using `UnoDeploy="Package"` mode will be deployed in the `dist\package_<hash>` folder and the folder structure will be preserved. This is the default mode for most files (see exclusions below).
* `Root`: files using `UnoDeploy="Root"` mode will be deployed directly in the `dist\` folder and the folder structure will be preserved.
* `None`: files using the `UnoDeploy="None"` mode will be ignored and won't be deployed.

Exclusions:

1. Files in the `WasmScript` folder will be set as `UnoDeploy="None"` by default (they are not treat as content)

2. Files in the `wwwroot` folder will be set as `UnoDeploy="Root"` by default

3. You can manually set the _deploy mode_ in the `.csproj` in the following way:

   ```xml
   <ItemGroup>
       <!-- Manually set a file to be deployed as "root" mode -->
       <Content Include="Path\To\My\File.txt" UnoDeploy="Root" />
   
       <!-- Manually set a file to be deployed as "package" mode -- overriding the default "root" mode for wwwroot -->
       <Content Include="wwwroot\config.json" UnoDeploy="Package" />
   
       <!-- Manually set a file to be deployed as "none" mode (not deployed) -->
       <Content Include="wwwroot\output.log" UnoDeploy="None" />
   </ItemGroup>
   ```

Asset files: `dist/package_XXXX/uno-assets.txt` contains the package relative paths of the content files that were copied to the  `dist/package_XXXX` folder. It can be used to identify which assets are packaged with the application at runtime and avoid costly probing operations. Important: Will only contain files deployed in `UnoDeploy="Package"` mode.

A few files extensions are excluded (`UnoDeploy="None")`by default such as `*.a`, `*.bc`.
 `.html` files are those named `web.config` will default to `UnoDeploy="Root"`.

> **IMPORTANT INFORMATION ABOUT BROWSER CACHE:** Browsers will often cache files based on their final url. When deploying a file in the `UnoDeploy="Root"` mode, beware the browser could cache the previous version. Files the the `UnoDeploy="Package"` mode won't have this problem since the path name contains a hash of their content. It important to take that into consideration when deciding to use the `UnoDeploy="Root"` mode.

### Support for deep-linking / routes

By default, deep-linking is not enabled to allow deployed apps to be hosted at any deployed locations. Anchor based navigation is supported by default, as it does not impact the path part of the URI.

To enable deep-linking or routes, add the following parameter to your project:
```xml
<PropertyGroup>
  <WasmShellWebAppBasePath>/</WasmShellWebAppBasePath>
</PropertyGroup>
```

This will for the all requests made to the bootstrapper's support files to be read from this path, regardless of the depth of the path. This way an address like `https://example.com/route1/item1` will be supported, assuming that the application is hosted at the root of the host.

One example of such support is done through the [Azure Static WebApps](https://platform.uno/docs/articles/guides/azure-static-webapps.html) support, using navigation fallback:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/package_*"]
  }
}
```

This parameter can be configured on build by using a command-line parameter as follows:

```bash
dotnet build "/p:WasmShellWebAppBasePath=/"
```

### Support for PWA Manifest File
A **Progressive Web App** manifest link definition can be added to the index.html file's head:
- Use the `WasmPWAManifestFile` property to set the file name
- Add a [Web App Manifest file](https://docs.microsoft.com/en-us/microsoft-edge/progressive-web-apps-chromium/get-started#step-2---create-a-web-app-manifest).
- Ensure the build action is `Content` for this file so it gets copied to the output folder. The `UnoDeploy="Package"` mode (which is the default) must be used. This file must not be put in the `wwwroot` folder.
- Create a set of icons using the [App Image Generator](https://www.pwabuilder.com/imageGenerator)

iOS's support for home screen icon is optionally set by searching for a 1024x1024 icon in the 
PWA manifest. Not providing this image will make iOS generate a scaled-down screenshot of the application.

You can validate your PWA in the [chrome audits tab](https://developers.google.com/web/updates/2017/05/devtools-release-notes#lighthouse). If your 
PWA has all the appropriate metadata, the PWA installer will prompt to install your app.

### Support for Subresource Integrity
By default, the _msbuild task_ will calculate a hash for binary files in your project and will use the [Subresource Integrity](https://www.w3.org/TR/SRI/)
to validate that the right set of files are loaded at runtime.

You can deactivate this feature by setting this property in your `.csproj` file:

```xml
<WashShellUseFileIntegrity>False</WashShellUseFileIntegrity>
```

### Support for Content prefetching
The `WashShellGeneratePrefetchHeaders` controls the generation of `<link rel="prefetch" />` nodes in the index.html header.

It is enabled by default and allows for the browser to efficiently fetch the applications
webassembly and .NET assemblies files, while the JavaScript and WebAssembly runtimes are 
being initialized.

This prefetching feature is particularly useful if the http server supports HTTP/2.0.

## Environment variables
Mono provides the ability to configure some features at initialization, such as logging or GC.

To set those variables, add the following to your project file:

```xml
<ItemGroup>
  <WasmShellMonoEnvironment Include="MONO_GC_PARAMS" Value="soft-heap-limit=512m,nursery-size=64m,evacuation-threshold=66,major=marksweep" />
  <WasmShellMonoEnvironment Include="MONO_LOG_LEVEL" Value="debug" />
  <WasmShellMonoEnvironment Include="MONO_LOG_MASK" Value="gc" />
</ItemGroup>
```

These lines change the configuration of the GC and logging, to determine when a GC occurs. More options are available 
in the `Environment Variables` section of [the mono documentation](http://docs.go-mono.com/?link=man%3amono(1)). 

### Configuration Environment Variables
The bootstrapper provides a set of environment variables that reflect the configuration provided at build time:

- `UNO_BOOTSTRAP_MONO_RUNTIME_MODE`, which specifies the runtime mode configuration (see above for valid values)
- `UNO_BOOTSTRAP_LINKER_ENABLED`, which is set to `True` if the linker was enabled, otherwise `False`
- `UNO_BOOTSTRAP_DEBUGGER_ENABLED`, which is set to `True` if the debugging support was enabled, otherwise `False`
- `UNO_BOOTSTRAP_MONO_RUNTIME_CONFIGURATION`, which provides the mono runtime configuration, which can be can either be `release` or `debug`.
- `UNO_BOOTSTRAP_MONO_PROFILED_AOT`, which specifies if the package was built using a PG-AOT profile.
- `UNO_BOOTSTRAP_APP_BASE`, which specifies the location of the app content from the base. Useful to reach assets deployed using the `UnoDeploy="Package"` mode.
- `UNO_BOOTSTRAP_WEBAPP_BASE_PATH`, which specifies the base location of the webapp. This parameter is used in the context of deep-linking (through the `WasmShellWebAppBasePath` property). This property must contain a trailing `/` and its default is `./`.
- `UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY`, which optionally specifies the maximum memory available to the WebAssembly module. 

Those variables can be accessed through [Environment.GetEnvironmentVariable](https://docs.microsoft.com/en-us/dotnet/api/system.environment.getenvironmentvariable).

### 4GB memory support

The support for 4GB memory space is available by adding the following configuration:
```xml
<ItemGroup>
	<WasmShellExtraEmccFlags Include="-s MAXIMUM_MEMORY=4GB"/>
</ItemGroup>
```
The configuration can also be detected at runtime using the `UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY` environment variable, which will be set to `4GB` once set.

See this [blog post from the v8 team](https://v8.dev/blog/4gb-wasm-memory) for more information.

### Dependency management
The Uno Bootstrapper uses RequireJS for dependency management, allowing for dependencies to be resolved in a stable manner. 

For instance, a script defined this way, placed in the `WasmScripts` folder:

```javascript
define(() => {
    var txt = document.createTextNode("Loaded !");
    var parent = document.getElementById('uno-body');
    parent.insertBefore(txt, parent.lastChild);
});
```

will be executed appropriately.

Dependencies can also be declared this way: 

```javascript
define([], function() { return MyModule; });
```

If you're taking a dependency on an [AMD](https://en.wikipedia.org/wiki/Asynchronous_module_definition) enabled library, you'll need to publish the library as it would outside of the normal require flow.

As an example, to be able to use [html2canvas](https://html2canvas.hertzen.com/):
- Add the `html2canvas.js` file as an `EmbeddedResource` in the `WasmScripts` folder
- Then create an additional file called `myapp.js`, also as an `EmbeddedResource`
  ```
  require([`${config.uno_app_base}/html2canvas`], c => window.html2canvas = c);
  ```
- You'll then be able to access `window.html2canvas` from C# using `eval()`.

### Dependency management for Emscripten

Emscripten modules initialization is performed in an asynchronous way and the Bootstrapper 
will ensure that a dependency that exposes a module will have finished its initialization 
for starting the `Main` method of the C# code.

### Index.html content override
The msbuild property `WasmShellIndexHtmlPath` can be used to specify the path of a project-specific `index.html` file.

This file should contain the following markers, for the runtime to initialize properly: 
- `$(ADDITIONAL_CSS)`
- `$(ADDITIONAL_HEAD)`

Use the [Templates/index.html](src/Uno.Wasm.Bootstrap/Templates/index.html) file as an example.

## Splash screen customization

The default configuration for the bootstrapper is to show the Uno Platform logo. This can be changed, along with the background color and progress bar color by doing the following:

- Create an AppManifest.js file in the `WasmScripts` folder
- Set its build action to `EmbeddedResource`
- Add the following content:
```
var UnoAppManifest = {
    splashScreenImage: "https://microsoft.github.io/microsoft-ui-xaml/img/winui-logo.png",
    splashScreenColor: "#00f",
    accentColor: "#f00",
}
```

These properties are supported in the manifest:
- `lightThemeBackgroundColor` (optional) to change the light theme background color
- `darkThemeBackgroundColor` (optional)to change the dark theme background color
- `splashScreenColor` to change the background color regardless of the theme. When set to `transparent`, `lightThemeBackgroundColor` and `darkThemeBackgroundColor` will be used, otherwise the default browser background color will be used.

Once the app start, the content will be updated to show the custom logo. The logo must be of size 630x300 (or same ratio).

### Configuration of the runtime
- The msbuild property `MonoRuntimeDebuggerEnabled` can be set to `true` to allow for mono to output additional debugging details, and have the debugger enabled (not supported yet by the mono tooling).
- The msbuild property `RuntimeConfiguration` allows for the selection of the debug runtime but is mainly used for debugging the runtime itself. The value can either be `release` or `debug`.

### Overriding the .NET WebAssembly SDK build
The msbuild property `NetCoreWasmSDKUri` allow the override of the default SDK path. The path can be a local file or remote file.

To select a different sdk build:
- For `net5` projects:
    - Generate a build from the `https://github.com/unoplatform/Uno.DotnetRuntime.WebAssembly` project
    - Copy the `dotnet-runtime-wasm-XX-XX-Release.zip` uri or local zip file path to the `NetCoreWasmSDKUri` property

> Note that override properties require a zip file as the source, not an uncompressed folder.

If you are overriding the contents of the uncompressed SDK folder, for debugging purposes, you will need to set the `WasmShellDisableSDKCheckSumValidation` property to true to avoid the bootstrapper to recreate the SDK folder.

### Changing the .NET SDKs install location
The SDKs are installed under `Path.GetTempPath()` by default, you may change this by setting the following msbuild property(or environment variable): `WasmShellMonoTempFolder`.

For example, on Windows, setting `WasmShellMonoTempFolder` to `C:\MonoWasmSDKs`, the `mono-wasm-e351637985e` sdk would be installed under `C:\MonoWasmSDKs\mono-wasm-e351637985e`
instead of `C:\Users\xxx\AppData\Local\Temp\mono-wasm-e351637985e`.

### Windows Long Path support
The bootstrapper supports Windows 10 long paths by default, but there may be cases where the 
[`\\?\` path format](https://blogs.msdn.microsoft.com/jeremykuhne/2016/06/21/more-on-new-net-path-handling/) may not be supported. 

In such a case, setting the `<WasmShellEnableLongPathSupport>false</WasmShellEnableLongPathSupport>` in the project file can disable this feature.

Additional documentation on the support for long paths [is available here](https://docs.microsoft.com/en-us/windows/win32/fileio/naming-a-file#enable-long-paths-in-windows-10-version-1607-and-later).

### WSL Integration for Windows 10

The integration with WSL provides a way for using AOT, Mixed mode or external bitcode support using Windows 10.

This feature is active only if one of those condition is true:
- The `WasmShellMonoRuntimeExecutionMode` property is `FullAOT` or `InterpreterAndAOT
- There is a `*.bc` or `*.a` file in the `Content` item group
- The `WasmShellForceUseWSL` is set to `true`

Otherwise, the WSL integration is not used and the mono runtime present in the SDK is used as-is.

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

Note that code executed through the interpreter will not appear explicitly in the performance charts, as it is executed through the interpreter. Only AOTed code will be visible.

## Native memory troubleshooting

To enable native memory troubleshooting, it is possible to use [LLVM's sanitizer](https://emscripten.org/docs/debugging/Sanitizers.html) feature.

To enable it, add the following to your project file:

```xml
<ItemGroup>
	<WasmShellExtraEmccFlags Include="-fsanitize=address" />
</ItemGroup>
```

This will allow for malloc/free and other related memory access features to validate for possible issues, like this one:

```
================================================================= dotnet.js:2498:16
==42==ERROR: AddressSanitizer: attempting free on address which was not malloc()-ed: 0x03116d80 in thread T0 dotnet.js:2498:16
    #0 0x1657f6 in free+0x1657f6 (http://localhost:57998/dotnet.wasm+0x1657f6) dotnet.js:2498:16
    #1 0x12eb3a in monoeg_g_free+0x12eb3a (http://localhost:57998/dotnet.wasm+0x12eb3a) dotnet.js:2498:16
    #2 0x19936 in ves_pinvoke_method+0x19936 (http://localhost:57998/dotnet.wasm+0x19936) dotnet.js:2498:16
    #3 0xb8a5 in interp_exec_method+0xb8a5 (http://localhost:57998/dotnet.wasm+0xb8a5) dotnet.js:2498:16
    #4 0xa0bb in interp_runtime_invoke+0xa0bb (http://localhost:57998/dotnet.wasm+0xa0bb) dotnet.js:2498:16
    #5 0x52fcf in mono_jit_runtime_invoke+0x52fcf (http://localhost:57998/dotnet.wasm+0x52fcf) dotnet.js:2498:16
    #6 0xc6a6f in do_runtime_invoke+0xc6a6f (http://localhost:57998/dotnet.wasm+0xc6a6f) dotnet.js:2498:16
    #7 0xc711a in mono_runtime_try_invoke+0xc711a (http://localhost:57998/dotnet.wasm+0xc711a) dotnet.js:2498:16
    #8 0xc9234 in mono_runtime_invoke+0xc9234 (http://localhost:57998/dotnet.wasm+0xc9234) dotnet.js:2498:16
    #9 0x7967 in mono_wasm_invoke_method+0x7967 (http://localhost:57998/dotnet.wasm+0x7967) dotnet.js:2498:16
    #10 0x80002ffa in Module._mono_wasm_invoke_method http://localhost:57998/dotnet.js:12282:51 dotnet.js:2498:16
    #11 0x800002e9 in ccall http://localhost:57998/dotnet.js:745:18 dotnet.js:2498:16
    #12 0x800002f4 in cwrap/< http://localhost:57998/dotnet.js:756:12
```

Showing that mono is trying to free some memory pointer that was never returned by `malloc`.

Note that the runtime performance is severely degraded when enabling this feature.

### musl weak aliases override

In some cases, the [musl library](https://github.com/emscripten-core/emscripten/tree/aa66f92b790c8d319d1599d44657a6305bf74a8a/system/lib/libc/musl) performs out-of-bounds reads which can be [detected as false positives](https://github.com/emscripten-core/emscripten/issues/7279#issuecomment-734934021) by ASAN's checkers.

In such cases, providing alternate non-overflowing versions of [weak aliased MUSL functions](https://github.com/emscripten-core/emscripten/blob/aa66f92b790c8d319d1599d44657a6305bf74a8a/system/lib/libc/musl/src/string/stpncpy.c#L31) is possible through the `WasmShellNativeCompile` feature of the bootstrapper.

For example, `strncpy` can be overriden by a custom implementation as follows:

```c
char *__stpncpy(char *restrict d, const char *restrict s, size_t n)
{
	for (; n && (*d=*s); n--, s++, d++);
	memset(d, 0, n);
	return d;
}
```

in a C file defined in your project and imported through `WasmShellNativeCompile`.
