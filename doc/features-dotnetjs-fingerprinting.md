---
uid: UnoWasmBootstrap.Features.DotnetJsFingerprinting
---

# dotnet.js Fingerprinting

The bootstrapper automatically fingerprints the `dotnet.js` file produced by the .NET SDK, rewriting references in `uno-config.js` to use the fingerprinted filename (e.g., `dotnet.abc123.js` instead of `dotnet.js`). This improves cache busting behavior so that browsers fetch the correct version of the runtime after an app update.

Fingerprinting is enabled by default, unless `WasmFingerprintAssets` is set to `false` (the .NET SDK then emits an unfingerprinted `dotnet.js`). To disable it explicitly, add the following to your `.csproj`:

```xml
<PropertyGroup>
    <WasmShellEnableDotnetJsFingerprinting>false</WasmShellEnableDotnetJsFingerprinting>
</PropertyGroup>
```

The .NET SDK [`WasmFingerprintDotnetJs`](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly) property is also supported.

When publishing into a directory that wasn't cleaned, `_framework` can contain `dotnet.*.js` files from earlier publishes. The bootstrapper reads the SDK's `*.staticwebassets.endpoints.json`, which every publish rewrites, to find the current one.

## Diagnostics

The following diagnostics may be emitted by the publish-time update:

| Code | Description |
|------|-------------|
| UNOWASM001 | Error: `uno-config.js` does not contain a fingerprinted `dotnet.js` reference after the update. |
| UNOWASM002 | Error: the fingerprint in `uno-config.js` does not match any `dotnet.*.js` file on disk. |
| UNOWASM003 | Warning: several `dotnet.*.js` files were found and no endpoints manifest tells which one is current; the newest is used. Clean the publish directory before publishing. |
