---
uid: UnoWasmBootstrap.Features.ResilientLoading
---

# Resilient loading

On an unstable connection (mobile handovers, congested Wi-Fi), some of the dozens of files an app downloads at startup can fail or stall. The bootstrapper downloads them so that this slows startup down rather than breaking it:

- Every download has a headers timeout and an idle watchdog: a response that stops sending bytes is abandoned instead of waited on forever.
- Failed downloads (network errors, connections reset mid-body, `5xx`, `408` and `429` responses, stalls) are retried with a jittered exponential backoff. While the browser reports being offline, retries wait for it to come back online.
- This covers the .NET runtime's resources (through its `withResourceLoader` hook), the runtime's own JavaScript modules, `uno-config.js`, `dotnet.js` and the `require.js` dependencies.
- Subresource integrity is still verified, by the bootstrapper rather than by `fetch`, which would hide download progress until the whole body arrived.

`index.html` and the files it references directly (`require.js`, `uno-bootstrap.js`, stylesheets) are loaded by the browser and are not retried.

## Configuration

The defaults can be overridden with environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `UNO_BOOTSTRAP_FETCH_HEADERS_TIMEOUT_MS` | `20000` | Time to wait for a response's headers. Also the time after which a JavaScript module import is considered stalled. |
| `UNO_BOOTSTRAP_FETCH_IDLE_TIMEOUT_MS` | `8000` | Time without receiving any bytes after which a download is considered stalled. |
| `UNO_BOOTSTRAP_FETCH_MAX_ATTEMPTS` | `8` | Attempts per file before startup fails. |

For example:

```xml
<ItemGroup>
    <WasmShellMonoEnvironment Include="UNO_BOOTSTRAP_FETCH_IDLE_TIMEOUT_MS" Value="15000" />
</ItemGroup>
```
