# Uno.Wasm.Bootstrap

Uno.Wasm.Bootstrap provides a simple way to package a C# .NET Standard 2.0 library, and run it from a compatible browser environment. 

It is a standalone Mono Web Assembly (WASM) sdk bootstrapper taking the form of a nuget package.

Installing it on a .NET Standard 2.0 library with an entry point allows to publish it as part of a WASM distribution folder, along with CSS, Javascript and content files.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`.

This package is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

## How to use the package
* Create a .NET Standard 2.0 library, and update it with the following basic definition:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netstandard2.0</TargetFramework>
    <MonoRuntimeDebuggerEnabled>false</MonoRuntimeDebuggerEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Uno.Wasm.Bootstrap" Version="1.0.0-dev.1" />

    <DotNetCliToolReference Include="Uno.Wasm.Bootstrap.Cli" Version="1.0.0-dev.1" />
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
* In visual studio, press `F5` or **Debug**, then **Start debugging**
* A browser window will appear with your application
* The output of the Console.WriteLine will appear in the javascript debugging console

### Alternate deployment path using Linux (or Windows Subsystem for Linux)
See below the instructions on how to install the **Windows Subsystem for Linux**.
* Build the project, the WASM output will be located in `bin\Debug\netstandard2.0\dist`.
* Run the `server.py`, which will open an HTTP server on http://localhost:8000. On Windows, use Python tools or the excellent Linux Subsystem.
* The output of the `Console.WriteLine` will appear in the javascript debugging console

### Upgrading from previous versions of the Uno.Wasm.Bootstrap package
Previously, the suggested project structure was a .NET Standard 2.0 project using the non-web projects SDK. To emable debugging and easier deployment, the support for `Microsoft.NET.Sdk.Web` has been added.

To upgrade a project:
- Change `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web` in the Sdk attribute of your project
- Add the `<DotNetCliToolReference Include="Uno.Wasm.Bootstrap.Cli" Version="1.0.0-dev.1" />` item in the same item group as the other nuget packages.

## Linker configuration
The mono-wasm tooling uses the [ILLinker](https://github.com/mono/linker/tree/master/linker), and can be configured using a linker directives file.

The Bootstrapper searches for an file placed in an ItemGroup named `LinkerDescriptor`, with the following sample content:

```xml
<linker>
	<assembly fullname="Uno.Wasm.Sample">
		<namespace fullname="Uno.Wasm.Sample" />
	</assembly>

	<assembly fullname="WebAssembly.Bindings" />
</linker>
```

The documentation for this file [can be found here](https://github.com/mono/linker/tree/master/src/linker#syntax-of-xml-descriptor).

## Publishing the build results
The easiest way to publish the build results is to use the Visual Studio publish menu on your  project. This will allow to use all the features provided by the standard experience, as described in the [Deploy to Azure App Service](https://docs.microsoft.com/en-us/visualstudio/deployment/quickstart-deploy-to-azure?view=vs-2017).

## Serve the Wasm app through Windows Linux Subsystem
Using Windows 10, serving the app through a small Web Server is done through WSL.

Here's how to install it:
- Search for Ubuntu in the Windows Store: https://www.microsoft.com/en-us/search/result.aspx?q=ubuntu
- Install Ubuntu 18.04 or later, and follow the instructions during the first run
- Once you've built your project, you should see a path to the project dll
- In the Ubuntu shell, type ``cd `wslpath "[the_path_to_your_bin_folder]\dist"` ``
- Type `python3 server.py`
	- If this command does not exist, run the following `sudo apt-get install python3`
- Using your favorite browser, navigate to `http://localhost:8000`

## Mono-wasm Debugger Support

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

Finally the `DEBUG` constant must be defined

```xml
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
   <DefineConstants>TRACE;DEBUG</DefineConstants>
</PropertyGroup>
```

Doing so will enable the deployment of `pdb` files to the browser, and allow for the mono debugger proxy to use them. 

For the time being, you will also need to make sure that mscorlib is disabled in the Linker configuration file: 

```xml
<!-- Required for debugging -->
<assembly fullname="mscorlib">
</assembly>
<assembly fullname="System.Core">
</assembly>
```

Mono-wasm now has integrated **preliminary** support for in-browser debugging. Refer to
[this document for up-to-date information](https://github.com/mono/mono/tree/master/sdks/wasm#debugging) on how to setup the debugging.

### How to use the debugger
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
- Breakpoints set sometimes disapear when the debugged page is reloaded
- If none of your assemblies appear in the debugger window, it's generally caused 
by the debugger caching previously loaded files. Make sure to hit Ctrl+Shit+R to force 
reload the debugged page.


## Runtime Execution Modes
The mono for WebAssembly runtime provides three execution modes, Interpreter, AOT and Mixed Mode Interpreter/AOT.

The execution mode can be set as follows:
```xml
<WasmShellMonoRuntimeExecutionMode>Interpreter</WasmShellMonoRuntimeExecutionMode>
```
The possible values are:
- `Interpreter` (the default mode)
- `FullAOT`
- `InterpreterAndAOT`

### Interpreter mode
This modes is the slowest of all three, but allows for a large flexibility and debugging, as well as an efficient payload size. 

The linker mode can also be completely disabled for troubleshooting, as this will not impact the wasm payload size.

### AOT Mode
This mode generates WebAssembly binary for all the referenced assemblies and provides the fastest code execution, but also generates the largest payload. This mode will not allow the execution of code that was not known at compile time (e.g. dynamically generated assemblies or loaded through `Assembly.LoadFrom`).

It is **currently only available on Linux** (18.04 and later, or similar).

To ensure that AOT is only run under Linux, add the following to your project:
```xml
<WasmShellMonoRuntimeExecutionMode>FullAOT</WasmShellMonoRuntimeExecutionMode>
```

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

## Required configuration for AOT Compilation
- A Linux 18.04 machine or [WSL 18.04](https://docs.microsoft.com/en-us/windows/wsl/install-win10)
- A [stable build of mono](https://www.mono-project.com/download/stable/#download-lin) with msbuild (`apt install msbuild`) >= 5.16
- A [dotnet core installation](https://docs.microsoft.com/en-us/dotnet/core/linux-prerequisites?tabs=netcore2x) above 2.2
- An active Emscripten 1.38.13 (specifically, until [Esmcripten #7656](https://github.com/emscripten-core/emscripten/pull/7656) gets merged)
- A [patch to the emscripten installation](https://github.com/mono/mono/blob/master/sdks/builds/fix-emscripten-7399.diff)
	- `cd emscripten/1.38.13; patch -N -p1 < fix-emscripten-7399.diff`

## Features
### WebAssembly Module Linking support

#### Dynamic Linking
Support for [Emscripten's dynamic linking](https://github.com/emscripten-core/emscripten/wiki/Linking) is included in the bootstrapper and is used for interpreter based builds.

Any `.wasm` file placed as content in the built project will be dynamically linked to 
the currently running application, allowing for p/invoke to be functional when resolving methods
from the loaded module.

For more information, please refer to the `Uno.Wasm.DynamicLinking` sample in the `Uno.Wasm.Bootstrap` solution.

#### Static Linking
Statically linking Emscripten LLVM Bitcode (`.bc` files) files to mono is supported when building for AOT and Mixed runtime modes.

This linking type embeds the `.bc` files with the rest of the WebAssembly modules, and uses _normal_
webassembly function invocations that are faster than with dynamic linking.

Any `.bc` file placed as content in the built project will be statically linked to 
the currently running application, allowing for p/invoke to be functional when resolving methods
from the loaded module.

For more information, see the `Uno.Wasm.DynamicLinking` sample side module build script.

### Support for IIS / Azure Webapp GZip/Brotli pre-compression
The IIS compression support has too many knobs for the size of generated WebAssembly files, which
makes the serving of static files inefficient.

The Bootstrapper tooling will generate two folders `_compressed_gz` and `_compressed_br` which contain compressed versions of the main files. A set IIS rewriting rules are used to redirect the queries to the requested pre-compressed files, with a preference for Brotli.

When building an application, place [the following file](src/Uno.Wasm.Sample/wwwroot/web.config) in the `wwwroot` folder to automatically enable the use of pre-compressed files.

The parameters for the compression are as follows:
- `WasmShellGenerateCompressedFiles` which can be `true` of `false`. This property is ignored when building `MonoRuntimeDebuggerEnabled` is set to `true`.
- `WasmShellCompressedExtension` is an item group which specifies which files to compress. By default `wasm`, `clr`, `js`, `css` and `html files are pre-compressed. More files can be added as follows:
```xml
  <ItemGroup>
    <WasmShellCompressedExtension Include=".db"/>
  </ItemGroup>
```
- `WasmShellBrotliCompressionQuality` which controls the compression quality used to pre-compress the files. The default value is 7.

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

The parameters of the node command line are provided to the app's main method, when running the app as follows:

```
node app param1 param2
```

An example of the node.js support is available in the `Uno.Wasm.Node.Sample` and `Uno.Wasm.Node.Sample.Runner.njsproj` projects.

### Support for additional JS files
Providing additional JS files is done through the inclusion of `EmbeddedResource`  msbuild item  files, in a project folder named `WasmScripts`.
Files are processed as embedded resources to allow for libraries to provide javascript files.

### Support for additional CSS files
Additional CSS files are supported through the inclusion of `EmbeddedResource`  msbuild item files, in a project folder named `WasmCSS`.

### Support for additional Content files
Additional CSS files are supported through the inclusion of `Content` files. The folder structure is preserved in the output `dist` folder.

### Support for PWA Manifest File
A **Progressive Web App** manifest link definition can be added to the index.html file's head:
- Use the `WasmPWAManifestFile` property to set the file name
- Add a [Web App Manifest file](https://docs.microsoft.com/en-us/microsoft-edge/progressive-web-apps/get-started#web-app-manifest) 
- Set the `Content` build action to this new file so it gets copied to the output folder
- Create a set of icons using the [App Image Generator](https://www.pwabuilder.com/imageGenerator)

iOS's support for home screen icon is optionally set by searching for a 1024x1024 icon in the 
PWA manifest. Not providing this image will make iOS generate a scaled down screenshot of the application.

You can validate you PWA in the [chrome audits tab](https://developers.google.com/web/updates/2017/05/devtools-release-notes#lighthouse). If your 
PWA has all the appropriate metadata, the PWA installer will prompt to install you app.

### Linker configuration
The linker may be configured via the inclusion of `LinkerDescriptor` msbuild item files.

The file format of the descriptor can [be found here](https://github.com/mono/linker/tree/master/linker#syntax-of-xml-descriptor).

The Linker can be disabled completely by setting the `WasmShellILLinkerEnabled` property to 
false. This property has no effect when building with AOT enabled.

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

Those variables can be accessed through [Environment.GetEnvironmentVariable](https://docs.microsoft.com/en-us/dotnet/api/system.environment.getenvironmentvariable).

### Dependency management
The Uno Bootstrapper uses RequireJS for the dependency management, allowing for dependencies to be resolved in a stable manner. 

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

### Dependency management for Emscripten

Emscripten modules initialization is performed in an asynchronous way, and the Bootstrapper 
will ensure that a dependency that exposes a module will have finished its initialization 
for starting the `Main` method of the C# code.

## Index.html content override
The msbuild property `WasmShellIndexHtmlPath` can be used to specify the path of a project-specific `index.html` file.

This file should contain the following markers, for the runtime to initialize properly: 
- `$(ADDITIONAL_CSS)`
- `$(ADDITIONAL_HEAD)`

Use the [Templates/Index.html](src/Uno.Wasm.Bootstrap/Templates/Index.html) file as an example.

### Configuration of the runtime
- The msbuild property `MonoRuntimeDebuggerEnabled` can be set to `true` to allow for mono to output additional debugging details, and have the debugger enabled (not supported yet by the mono tooling).
- The msbuild property `RuntimeConfiguration` allows for the selection of the debug runtime, but is mainly used for debugging the runtime itself. The value can either be `release` or `debug`.

### Updating the mono-sdk build
The msbuild properties `MonoWasmSDKUri` and `MonoWasmAOTSDKUri` allow the override of the default SDK paths. Paths can be local files.

To select a different sdk build:
- Navigate to the [Mono-wasm CI](https://jenkins.mono-project.com/job/test-mono-mainline-wasm/)
- Select a build
- Click on the "default" configuration
- On the left click *Azure Artifacts*
- Copy the `mono-wasm-xxxx.zip` uri or local zip file path to the `MonoWasmSDKUri` property
- Copy the `wasm-release-Linux-xxx.zip` uri or local zip file to the `MonoWasmAOTSDKUri` property

> Note that both properties require a zip file as the source, not an uncompressed folder.

## Updating the Uno.Wasm.Boostrapper default mono-wasm SDK
The bootstrapper comes with a default mono-wasm SDK (which can be overriden per project with the msbuild properties
`MonoWasmSDKUri` and `MonoWasmAOTSDKUri`), specified in the `Constants.cs` file.

To update to a later mono-wasm SDK:
- Navigate to the [Mono-wasm CI](https://jenkins.mono-project.com/job/test-mono-mainline-wasm/)
- Copy the `mono-wasm-xxxx.zip` uri to the `DefaultSdkUrl` constant field
- Copy the `wasm-release-Linux-xxx.zip` uri to the `DefaultAotSDKUrl` constant field
- Open the `mono-wasm-xxxx.zip` and copy the `Mono.WebAssembly.DebuggerProxy.dll` and `.pdb` to the [CustomDebuggerProxy folder](src/Uno.Wasm.Bootstrap/build/CustomDebuggerProxy) folder.
