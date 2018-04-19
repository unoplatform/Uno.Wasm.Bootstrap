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

## Features
### Support for additional JS files
Providing additional JS files is done through the inclusion of `EmbeddedResource`  msbuild item  files, in a project folder named `WasmScripts`.
Files are processed as embedded resources to allow for libraries to provide javascript files.

### Support for additional CSS files
Additional CSS files are supported through the inclusion of `EmbeddedResource`  msbuild item files, in a project folder named `WasmCSS`.

### Support for additional Content files
Additional CSS files are supported through the inclusion of `Content` files. The folder structure is preserved in the output `dist` folder.

### Linker configuration
The linker may be configured via the inclusion of `LinkerDescriptor` msbuild item files.

The file format of the descriptor can [be found here](https://github.com/mono/linker/tree/master/linker#syntax-of-xml-descriptor).

## Index.html content override
The msbuild property `WasmShellIndexHtmlPath` can be used to specify the path of a project-specific `index.html` file.

This file should contain the following markers, for the runtime to initialize properly: 
- `$(ASSEMBLIES_LIST)`
- `$(MAIN_ASSEMBLY_NAME)`
- `$(MAIN_NAMESPACE)`
- `$(MAIN_TYPENAME)`
- `$(MAIN_METHOD)`
- `$(ENABLE_RUNTIMEDEBUG)`
- `$(ADDITIONAL_SCRIPTS)`
- `$(ADDITIONAL_CSS)`

Use the [Templates/Index.html](src/Uno.Wasm.Bootstrap/Templates/Index.html) file as an example.

### Configuration of the runtime
- The msbuild property `RuntimeDebugLogging` can be set to `true` to allow for mono to output additional debugging details.
- The msbuild property `RuntimeConfiguration` allows for the selection of the debug runtime, but is mainly used for debugging the runtime itself. The value can either be `release` or `debug`.
- The msbuild property `MonoWasmSDKUri` allows the override of the default SDK path.

## TODO
Lots! 
- The main missing part is the ability to change the index.html, but it should pretty easy to add.
- The other one is the ability to use an actual release of the mono-wasm release.