# Uno.Wasm.Bootstrap

Uno.Wasm.Bootstrap provides a simple way to package a C# .NET Standard 2.0 library, and run it from a compatible browser environment. 

It is a standalone Mono Web Assembly (WASM) sdk bootstrapper taking the form of a nuget package.

Installing it on a .NET Standard 2.0 library with an entry point allows to publish it as part of a WASM distribution folder, along with CSS, Javascript and content files.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`.

This package is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

## How to use the package
* Create a .NET Standard 2.0 library, with the following basic definition:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Uno.Wasm.Bootstrap" Version="1.0.0-dev.1" />
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
* Build the project, the WASM output will be located in `bin\Debug\netstandard2.0\dist`.
* Run the `server.py`, which will open an HTTP server on http://localhost:8000.  On Windows, use Python tools or the excellent Linux Subsystem.
* The output of the Console.WriteLine will appear in the javascript debugging console

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

The documentation for this file [can be found here](https://github.com/mono/linker/tree/master/linker#syntax-of-xml-descriptor).

## Server the Wasm app through Windows Linux Subsystem
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
Mono-wasm now has integrated **preliminary** support for in-browser debugging. Refer to
[this document for up-to-date information](https://github.com/mono/mono/tree/master/sdks/wasm#debugging) on how to setup the debugging.

To enable debugging in **Uno.Wasm.Boostrap**, add the following line to your csproj:

```xml
<MonoRuntimeDebuggerEnabled>true</MonoRuntimeDebuggerEnabled>
```

This will enable the deployment of `pdb` files to the browser, and allow for the [debugger proxy](https://github.com/kumpera/ws-proxy) to pick those up.

For the time being, you will also need to make sure that mscorlib is disabled in the Linker configuration file: 

```xml
<!-- Required for debugging -->
<assembly fullname="mscorlib">
</assembly>
<assembly fullname="System.Core">
</assembly>
```

## AOT Support
The mono-wasm tooling now provides AOT support for WebAssembly. This mode can be enabled by using the `WasmShellEnableAOT` property, but is **currently only available on Linux** (18.04 and later, or similar).

To ensure that AOT is only run under Linux, add the following to your project:
```xml
<WasmShellEnableAOT Condition="$([MSBuild]::IsOsPlatform('Linux'))">true</WasmShellEnableAOT>
```

The machine needs [ninja build](https://ninja-build.org/) installed, as well as a [registered Emscripten installation](https://kripken.github.io/emscripten-site/docs/getting_started/downloads.html).

## Features
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

### Linker configuration
The linker may be configured via the inclusion of `LinkerDescriptor` msbuild item files.

The file format of the descriptor can [be found here](https://github.com/mono/linker/tree/master/linker#syntax-of-xml-descriptor).

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