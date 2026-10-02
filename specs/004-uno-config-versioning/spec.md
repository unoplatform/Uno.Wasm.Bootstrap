# Spec: Content-versioned uno-config.js

## Problem

`uno-config.js` lives in `package_<hash>/`, which hosts cache as immutable. Its content changes on every build (it names the fingerprinted `dotnet.js`) while the package hash does not, so returning visitors load a stale config and fail with a 404 on a `dotnet.js` that no longer exists.

## Requirements

1. `uno-config.js` is always loaded as `uno-config.js?v=<version>`, where the version is the first 12 hex characters of the SHA-256 of the file.
2. The version is stamped on the entry points that are never cached as immutable:
   - `index.html` (Browser mode): `uno-bootstrap.js?v=<version>` and `uno-config.js?v=<version>`.
   - `service-worker.js`: its `uno-config.js` import.
   - `embedded.js` (BrowserEmbedded mode): its `uno-bootstrap.js` import.
3. The bootstrapper forwards the version of its own URL to its `uno-config.js` import. In Browser mode it reads the `uno-bootstrap.js` module script; in BrowserEmbedded mode it reads `document.uno_bootstrap_url`, set by `embedded.js`.
4. The version is recomputed after `uno-config.js` is rewritten (dotnet.js fingerprinting, on build and publish), and previously stamped versions are replaced.
5. Offline (PWA): the service worker precaches `uno-config.js` unversioned. On a failed fetch it first matches the exact request, and only a `uno-config.js` request falls back to ignoring the query string. Other URLs keep exact matching.
6. Stale compressed copies (`.br`, `.gz`) of `uno-config.js` are deleted when it is rewritten.

## Edge cases

- No `uno-config.js` (or a referencing file is missing): nothing is stamped.
- Re-running the stamping is idempotent.
- Bootstrapper without a version (older host page): imports the unversioned URL.

## Validation

- `Uno.Wasm.Bootstrap.UnitTests` (`Given_UnoConfigVersion`): hashing and reference rewriting.
- `Uno.Wasm.Tests.Fingerprint/test-fingerprint.sh`: publish output carries a matching version in the entry points.
