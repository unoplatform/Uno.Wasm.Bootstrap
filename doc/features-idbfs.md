---
uid: UnoWasmBootstrap.Features.idbfs
---

# Support for IDBFS

In order to support emulated filesystem using [IDBFS](https://emscripten.org/docs/api_reference/Filesystem-API.html#filesystem-api-idbfs), add the following to your `.csproj`:

```xml
<PropertyGroup>
  <WasmShellEnableIDBFS>true</WasmShellEnableIDBFS>
</PropertyGroup>
```

Note that this property is a shortcut to this equivalent configuration:

```xml
<ItemGroup>
    <WasmShellExtraEmccFlags Include="-lidbfs.js" />
    <WasmShellEmccExportedRuntimeMethod Include="IDBFS" />
</ItemGroup>
```
