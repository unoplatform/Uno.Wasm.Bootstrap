---
uid: UnoWasmBootstrap.PackageOverrides
---

# Nuget package runtime overrides

By default, when presented with an assembly present in both the runtime and a nuget package, the bootstrapper will favor the runtime's version of the assembly. This is generally required to avoid internal consistency errors with the runtime.

In some rare cases though, it may still be required to override the runtime's version. To do this, you'll need to add the following to your csproj:

```xml
<ItemGroup>
  <!-- Note that the assembly file must include the .dll extension -->
  <WasmShellRuntimeCopyExclude Include="System.Text.Json.dll"/>
</ItemGroup>
```

This will ensure that the System.Text.Json.dll assembly coming from an explicit `PackageReference` will be favored over the runtime version.
