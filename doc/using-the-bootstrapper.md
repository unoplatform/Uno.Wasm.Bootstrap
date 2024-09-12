---
uid: UnoWasmBootstrap.Overview
---

# Using the bootstrapper

The Uno.Wasm.Bootstrap provides a base bootstrapping of the `Microsoft.NET.Sdk.WebAssembly` SDK provided by .NET 9.

This package only provides the bootstrapping features to run a .NET assembly and write to the javascript console, through `Console.WriteLine`. To write an app that provides UI functionalities, make sur to check out https://aka.platform.uno/get-started.

This work is based on the excellent work from @praeclarum's [OOui Wasm MSBuild task](https://github.com/praeclarum/Ooui).

## Prepare your machine

On the command line, type the following to install the WebAssembly workload:

```bash
dotnet workload install wasm-tools
```

## How to use the Bootstrapper with .NET 9 and later

- Create a .NET 9 Console Application, and update it with the following basic definition:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk.WebAssembly">

    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <TargetFramework>net9.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Uno.Wasm.Bootstrap" Version="9.0.*" />
  </ItemGroup>

  </Project>
  ```

- Add a main entry point:

  ```csharp
  class Program
  {
      static void Main(string[] args)
      {
          Console.WriteLine("Hello from C#!");
      }
  }
  ```

- In Visual Studio 2022, press `F5` to start with the debugger
- A browser window will appear with your application
- The output of the Console.WriteLine will appear in the javascript debugging console

## How to use the Visual Studio 2022 Debugger

To enable the debugging, make sure that the following line is present in your `Properties/launchSettings.json` file:

```json
"inspectUri": "{wsProtocol}://{url.hostname}:{url.port}/_framework/debug/ws-proxy?browser={browserInspectUri}"
```

in every profile section of the file, below each `"launchBrowser": true,` line.

Press `F5` to start debugging.

## Alternate deployment methods

Install the [`dotnet serve`](https://github.com/natemcmaster/dotnet-serve) tool:

```dotnetcli
dotnet tool install -g dotnet-serve
```

Once installed, launch the server by using the following command:

```bash
cd MyApp.Wasm
dotnet publish -c Debug
dotnet serve -d bin\Debug\net9.0\publish\wwwroot -p 8000
```

You application will be available `http://localhost:8000`.

## Upgrading from previous versions of the Uno.Wasm.Bootstrap package

Moving from version 8.x to 9.x may require changing the used MSBuild SDK for your project.

- If your project contains `Sdk="Uno.Sdk"`, you will need to update the Uno.Sdk to 5.5 or later.
- If your project contains `Sdk="Microsoft.NET.Sdk.Web"`, you'll need to change it to `Sdk="Microsoft.NET.Sdk.WebAssembly"`.

Once done, make sure to install the WebAssembly tools from .NET:

```bash
dotnet workload install wasm-tools
```

By default, the .NET runtime does not load all resource assemblies, but if you want to load all resources regardless of the user's culture, you can add the following to your project file:

```xml
<PropertyGroup>
    <WasmShellLoadAllSatelliteResources>true</WasmShellLoadAllSatelliteResources>
</PropertyGroup>
```

### Deprecated APIs

- The `Uno.Wasm.Boostrap.DevServer` package is not needed anymore and can be removed
- `WasmShellOutputPackagePath` has been removed. Use `$(PublishDir)`
- `WasmShellOutputDistPath` has been removed. Use `$(PublishDir)`
- `WasmShellBrotliCompressionQuality`, `WasmShellCompressedExtension` and `WasmShellGenerateCompressedFiles` have been removed, the compression is done by the .NET SDK
- `WasmShellEnableAotGSharedVT` has been removed.
- `WasmShellEnableEmscriptenWindows` has been removed, the .NET SDK manages emscripten
- `WasmShellEnableLongPathSupport` has been removed, the .NET SDK manages the build
- `WasmShellEnableNetCoreICU`has been removed, the .NET SDK manages localization
- `WasmShellForceDisableWSL` and `WasmShellForceUseWSL` have been removed, Windows and Linux are supported natively by the .NET SDK.
- `WashShellGeneratePrefetchHeaders` has been removed.
- `WasmShellNinjaAdditionalParameters` has been removed, the .NET SDK manages the build
- `WasmShellObfuscateAssemblies`, `WasmShellAssembliesExtension` and `AssembliesFileNameObfuscationMode` have been removed, the .NET SDK uses WebCIL to achieve the same result.
- `WasmShellPrintAOTSkippedMethods` has been removed
- `WasmShellPThreadsPoolSize` has been removed in favor of the official .NET SDK parameters
- `MonoRuntimeDebuggerEnabled` has been removed, the .NET SDK manages the debugger
- `WashShellUseFileIntegrity` has been removed, the .NET SDK manages the assets integrity

## Bootstrapper versions and .NET runtimes

Each major version of the bootstrapper targets a different version of the .NET Runtime.

- 2.x: Mono runtime (https://github.com/mono/mono)
- 3.x: .NET 6 (https://github.com/dotnet/runtime/commits/release/6.0)
- 7.x: .NET 7 (https://github.com/dotnet/runtime/commits/release/7.0)
- 8.0: .NET 8 (https://github.com/dotnet/runtime/commits/release/8.0)
- 9.0: .NET 9 (https://github.com/dotnet/runtime/commits/release/9.0)

> [!NOTE]
> Between version 3.x and 8.x, the bootstrapper is using custom builds of the runtime, maintained here: https://github.com/unoplatform/Uno.DotnetRuntime.WebAssembly
>
> [!NOTE]
> Bootstrapper builds version 4.x-dev were based on developments builds of .NET 7 and were later versioned 7.x-dev to match the appropriate runtime.

## Previous releases documentation

- [8.0.x](https://github.com/unoplatform/Uno.Wasm.Bootstrap/tree/release/stable/8.0/doc)
- [7.0.x](https://github.com/unoplatform/Uno.Wasm.Bootstrap/tree/release/stable/7.0/doc)
- [3.x](https://github.com/unoplatform/Uno.Wasm.Bootstrap/tree/release/stable/3.3/doc)
