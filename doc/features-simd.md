---
uid: UnoWasmBootstrap.Features.Overview
---

# Support for SIMD

Starting from .NET 7, support for SIMD is available through the following property:

```xml
<PropertyGroup>
    <WasmShellEnableSimd>true</WasmShellEnableSimd>
</PropertyGroup>
```

With .NET 8, SIMD support is enabled by default and can be disabled using:

```xml
<PropertyGroup>
    <WasmShellEnableSimd>false</WasmShellEnableSimd>
</PropertyGroup>
```

[WebAssembly Support for SIMD](https://github.com/webassembly/simd) enables faster execution for specialized pieces of code, and .NET increasingly uses those instructions to make applications run faster.

You can take a look at [this article](https://platform.uno/blog/safari-16-4-support-for-webassembly-fixed-width-simd-how-to-use-it-with-c/) for more information.

## Restriction for the use of SIMD

While SIMD is supported by all major browsers by default, some security modes can require disabling it.

For instance, Microsoft Edge's [Enhanced Security mode](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-security-browse-safer) disables the use of SIMD, as well as iOS's [Lockdown mode](https://support.apple.com/en-us/HT212650).
