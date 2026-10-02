# Feature Specification: Resilient Loading of Boot Resources

**Status**: Implemented
**User docs**: `doc/features-resilient-loading.md`
**Automated test**: `src/Uno.Wasm.Tests.ResilientLoading` (run by `run-test.sh`, wired in `build/ci/stage-build-linux-tests.yml`)

## Overview

On an unstable connection, one failed or stalled file among the dozens downloaded at startup breaks the boot or hangs it forever. The .NET loader only retries failures that happen before the response headers arrive, and nothing has a timeout. The bootstrapper (`ResourceLoader.ts`) downloads boot resources so that a bad connection slows startup down instead of breaking it.

## Retry contract

- Retried: network errors, connections reset mid-body, `5xx`, `408` and `429` responses, and stalls.
- Not retried: other `4xx` responses, and `index.html` with the files it references directly (`require.js`, `uno-bootstrap.js`, stylesheets), which the browser loads.
- Backoff is exponential with jitter, up to `UNO_BOOTSTRAP_FETCH_MAX_ATTEMPTS` attempts per file (default 8). While the browser is offline, retries wait for it to come back online.
- After the last attempt, startup fails with the last error.

## Timeout semantics

- Headers timeout (`UNO_BOOTSTRAP_FETCH_HEADERS_TIMEOUT_MS`, default 20000): time to wait for response headers. Also the time after which a JavaScript module import is considered stalled.
- Idle timeout (`UNO_BOOTSTRAP_FETCH_IDLE_TIMEOUT_MS`, default 8000): time without any received bytes after which a download is abandoned and retried. Slow but progressing downloads are never cut off.
- Invalid or non-positive overrides fall back to the defaults.

## Compatibility

- Subresource integrity is verified by the bootstrapper, since `fetch` would hide progress until the whole body arrived.
- `dotnet.native.wasm` is handed to the runtime as a cached response fetched by URL, so the browser keeps reusing its compiled-code cache.
- `uno-bootstrap.js` imported after the `load` event must still boot.

## Acceptance scenarios

```gherkin
Given a published app served so that the first request of every boot resource fails
  with a connection reset mid-body, a stall, or a 503 in turn
When the page is loaded
Then the app starts
And the test confirms that all three fault modes were injected
```

```gherkin
Given the same server
When uno-bootstrap.js is imported after the load event (/late.html)
Then the app starts
```
