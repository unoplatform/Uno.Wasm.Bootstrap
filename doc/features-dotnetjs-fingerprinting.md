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

## Where the configuration lives

`uno-config.js` is written to the root of `wwwroot`, next to `index.html`, rather than inside the `package_<hash>/` folder. The fingerprint produced by `dotnet publish` differs from the one known at build time, so the file is rewritten after the package hash has been computed. A file inside a folder that hosts cache as immutable must never change, so the configuration is kept with the root files, which hosts always revalidate. See [Publishing the build results](deploy-and-publish.md#caching-the-published-output) for the cache policy each folder expects.

## Diagnostics

The following build errors may be emitted if validation fails after the publish-time update:

| Code | Description |
|------|-------------|
| UNOWASM001 | `uno-config.js` does not contain a fingerprinted `dotnet.js` reference after the update. |
| UNOWASM002 | The fingerprint in `uno-config.js` does not match any `dotnet.*.js` file on disk. |
