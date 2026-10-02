---
uid: UnoWasmBootstrap.Features.Publish
---

# Publishing the build results

The easiest way to publish the build results is to use the Visual Studio publish menu on your project. This will allow to use all the features provided by the standard experience, as described in the [Deploy to Azure App Service](https://docs.microsoft.com/en-us/visualstudio/deployment/quickstart-deploy-to-azure?view=vs-2017).

In the command line, to publish the app, use the following:

```bash
dotnet publish
```

The app will be located in the `bin/Release/net10.0/publish/wwwroot` folder. More information about `dotnet publish` can be [found in the Microsoft docs](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish).

Publish into an empty folder: files left over from an earlier publish are deployed along with the new ones.

## Caching

Most published files can be cached by browsers and CDNs as immutable, because their URL changes when their content does:

- `package_<hash>/**`: the hash covers the package's static files. `uno-config.js`, which changes with every build, is loaded as `uno-config.js?v=<content hash>`.
- `_framework/*.<fingerprint>.*`: fingerprinted by the .NET SDK, unless `WasmFingerprintAssets` is `false`, in which case `_framework` must be revalidated like the entry points below.

The entry points must be revalidated on every load (for example `Cache-Control: no-cache`), since they carry the URLs of everything else:

- `index.html` (and any route falling back to it)
- `service-worker.js`
- `staticwebapp.config.json`, `web.config` and other files at the root

For example, for Azure Static Web Apps:

```json
{
  "routes": [
    { "route": "/package_*", "headers": { "cache-control": "public, max-age=31536000, immutable" } },
    { "route": "/_framework/*", "headers": { "cache-control": "public, max-age=31536000, immutable" } },
    { "route": "/*", "headers": { "cache-control": "no-cache" } }
  ]
}
```

> [!NOTE]
> Before `uno-config.js` was versioned, caching `package_*` as immutable made returning visitors load the previous deployment's `uno-config.js`, and fail with a 404 on its `dotnet.js` once that file was removed.

## Preload links

Publishing adds `<link rel="preload">` and `<link rel="modulepreload">` hints to `index.html` for the files of the startup chain: `uno-config.js`, `dotnet.js`, the .NET runtime's JavaScript modules, `dotnet.native.wasm` and the `require.js` dependencies. Without them, the browser discovers these files one round-trip at a time. The hints are inserted before `</head>`, between `<!-- uno-preload-links -->` markers.

To disable them:

```xml
<PropertyGroup>
    <WasmShellGeneratePreloadLinks>false</WasmShellGeneratePreloadLinks>
</PropertyGroup>
```

## Localization publishing

By default, the .NET runtime does not load all resource assemblies, but if you want to load all resources regardless of the user's culture, you can add the following to your project file:

```xml
<PropertyGroup>
    <WasmShellLoadAllSatelliteResources>true</WasmShellLoadAllSatelliteResources>
</PropertyGroup>
```

## Integration with ASP.NET Core

ASP.NET Core hosting is supported through the `Uno.Wasm.Bootstrap.Server` package.

In order to host an Uno Platform App, you'll need to the following:

- Create an `ASP.NET Core Web API` project (call it `MyApp.Server`). You may need to disable swagger for the `index.html` to be served properly.
- Add a NuGet reference to `Uno.Wasm.Bootstrap.Server`
- In your `Program.cs` startup, add the following to setup your `WebApplication` instance:

  ```csharp
  using Uno.Wasm.Bootstrap.Server;
  ...
  app.UseUnoFrameworkFiles();
  app.MapFallbackToFile("index.html");
  ```

- Add a project reference to the `Wasm` project
- Build and deploy `MyApp.Server`
