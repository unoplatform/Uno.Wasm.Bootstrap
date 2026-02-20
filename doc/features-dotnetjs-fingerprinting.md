---
uid: UnoWasmBootstrap.Features.DotnetJsFingerprinting
---

# dotnet.js Fingerprinting

The bootstrapper automatically fingerprints the `dotnet.js` file produced by the .NET SDK, rewriting references in `uno-config.js` to use the fingerprinted filename (e.g., `dotnet.abc123.js` instead of `dotnet.js`). This improves cache busting behavior so that browsers fetch the correct version of the runtime after an app update.

Fingerprinting is enabled by default. To disable it, add the following to your `.csproj`:

```xml
<PropertyGroup>
    <WasmShellEnableDotnetJsFingerprinting>false</WasmShellEnableDotnetJsFingerprinting>
</PropertyGroup>
```

The .NET SDK [`WasmFingerprintDotnetJs`](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly) property is also supported.

## Diagnostics

The following build errors may be emitted if validation fails after the publish-time update:

| Code | Description |
|------|-------------|
| UNOWASM001 | `uno-config.js` does not contain a fingerprinted `dotnet.js` reference after the update. |
| UNOWASM002 | The fingerprint in `uno-config.js` does not match any `dotnet.*.js` file on disk. |
