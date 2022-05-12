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
