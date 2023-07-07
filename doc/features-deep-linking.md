---
uid: UnoWasmBootstrap.Features.DeepLinking
---

# Support for deep-linking (routes)

Deep-linking enables the path part of the URI to indicate a location that should be navigated to. 

> [!TIP]
> This feature is colloquially referred to as _routing_ in the web development world.

## Use in Uno Platform applications

Apps using deep-linking typically parse the URI as part of a robust navigation system. No longer is access to resources on discrete pages complicated by repetitive UI steps. Instead, these areas can be navigated directly from a link in an email, a bookmark, or another website. When planning the capabilities of your application, it is essential to decide whether common scenarios necessitate deep-linking. 

If so, consider a navigation [system](xref:Overview.Navigation) that allows mapping route names to specific sectors of UI elements.

## Configure deep-linking

This feature is enabled by default in new projects generated from the `unoapp` template with version 4.9 or later.

Certain cases may require disabling this feature, such as when the application is hosted in a subdirectory of the host. This can be done by removing the `WasmShellWebAppBasePath` property from the `.csproj` file.

For project created from older template, add the following parameter to your project file to enable deep-linking:

```xml
<PropertyGroup>
  <WasmShellWebAppBasePath>/</WasmShellWebAppBasePath>
</PropertyGroup>
```

### Build-time configuration

This parameter can be configured on build by using a command-line parameter as follows:

```bash
dotnet build "-p:WasmShellWebAppBasePath=/"
```

## Behavior

All requests made to the bootstrapper's support files are read from the path specified by the `WasmShellWebAppBasePath` property. Restrictions are not placed on the depth of the path, so an address like `https://example.com/route1/item1` will be supported, assuming that the application is hosted at the root of the host.

One example of such support is done through the [Azure Static WebApps](https://platform.uno/docs/articles/guides/azure-static-webapps.html) support, using navigation fallback:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/package_*"]
  }
}
```

### Anchor based navigation

When deep-linking is disabled, anchor-based navigation remains supported, as it does not impact the path part of the URI.

## See also

- [Azure Static WebApps](xref:Uno.Tutorials.AzureStaticWepApps)
- [Navigation](xref:Overview.Navigation)
- [RouteMap](xref:Reference.Navigation.RouteMap)