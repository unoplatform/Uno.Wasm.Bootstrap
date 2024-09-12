---
uid: UnoWasmBootstrap.Features.Publish
---

# Publishing the build results

The easiest way to publish the build results is to use the Visual Studio publish menu on your project. This will allow to use all the features provided by the standard experience, as described in the [Deploy to Azure App Service](https://docs.microsoft.com/en-us/visualstudio/deployment/quickstart-deploy-to-azure?view=vs-2017).

In the command line, to publish the app, use the following:

```bash
dotnet publish
```

The app will be located in the `bin/Release/net9.0/publish/wwwroot` folder. More information about `dotnet publish` can be [found in the Microsoft docs](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish).

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
