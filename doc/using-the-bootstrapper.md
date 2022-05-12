Uno.Wasm.Bootstrap provides a simple way to package C# .NET code, and run it from a compatible browser environment.

It is a standalone .NET Web Assembly (Wasm) sdk bootstrapper taking the form of a nuget package.

Installing it on a .NET project (5, 6 or .NET Standard 2.0) with an entry point allows to publish it as part of a Wasm distribution folder, along with CSS, Javascript and content files.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`.

This package is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

## How to use the bootstrapper with .NET 5 and later
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

### How to use the Visual Studio 2019/2022 Debugger
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

### Changing the .NET SDKs install location
The SDKs are installed under `Path.GetTempPath()` by default, you may change this by setting the following msbuild property(or environment variable): `WasmShellMonoTempFolder`.

For example, on Windows, setting `WasmShellMonoTempFolder` to `C:\MonoWasmSDKs`, the `mono-wasm-e351637985e` sdk would be installed under `C:\MonoWasmSDKs\mono-wasm-e351637985e`

### Bootstrapper versios and .NET runtimes
Each major version of the bootstrapper targets a different version of the .NET Runtime.

- 2.x: Mono runtime (https://github.com/mono/mono)
- 3.x: .NET 6 (https://github.com/dotnet/runtime)
- 4.x-dev: .NET 7 (https://github.com/dotnet/runtime)

Starting from version 3.x, the bootstrapper uses a custom build of the runtime, maintained here: https://github.com/unoplatform/Uno.DotnetRuntime.WebAssembly