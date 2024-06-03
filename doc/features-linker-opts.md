---
uid: UnoWasmBootstrap.Features.LinkerOpts
---

# Emscripten Linker optimizations flags

When building with AOT or using static linking of bitcode files, the emscripten linker step is enabled and runs optimizations on the generated code.

These steps can be very expensive depending on the final binary size, and disabling those optimizations can be useful to improve the development loop.

To control those optimizations, use the following msbuild property:

```xml
<PropertyGroup>
    <WasmShellEmccLinkOptimization>false</WasmShellEmccLinkOptimization>
</PropertyGroup>
```

This flag is automatically set to `false` when running in a configuration named `Debug`.

The optimization level can be adjusted with the following:

```xml
<PropertyGroup>
    <WasmShellEmccLinkOptimizationLevel>Level3</WasmShellEmccLinkOptimizationLevel>
</PropertyGroup>
```

Allowed values are:

- `None` (`-O0`)
- `Level1` (`-O1`)
- `Level2` (`-O2`)
- `Level3` (`-O3`)
- `Maximum` (`-Oz`)
- Any other value will be passed onto emscripten without modifications

The default value is `Level3`.
