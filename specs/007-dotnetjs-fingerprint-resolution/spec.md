# Feature Specification: Published dotnet.js Fingerprint Resolution

**Status**: Implemented

## Overview

When the .NET SDK fingerprints framework assets, the published runtime is `_framework/dotnet.<fingerprint>.js`. Publishing into a directory that was not cleaned can leave several such files side by side. The bootstrapper must write the fingerprint of the file belonging to the current publish into `uno-config.js`, never that of a leftover.

## Requirements

- The task `ResolvePublishedDotnetJsTask_v0` reads the `*.staticwebassets.endpoints.json` files in the publish directory (one per published web project, so a hosted server may add several).
- The project's own manifest (`$(TargetName).staticwebassets.endpoints.json`) is authoritative when it maps `dotnet.js`: manifests left behind by renamed projects or other projects published to the same directory are then ignored. Otherwise the other manifests are used, newest first.
- A manifest that is not valid JSON (for example, truncated by an interrupted publish) is ignored with `UNOWASM005` instead of failing the build.
- A manifest maps the `_framework/dotnet.js` route to the current file. Each selected manifest mapping is checked against the `dotnet.*.js` files present; the first mapping that matches a file wins, regardless of file timestamps.
- If the selected manifests map `dotnet.js` but none of the mapped files exist, the publish is incomplete: the build fails with `UNOWASM004`. Leftover candidates are never used in that case.
- Without any manifest mapping, a single `dotnet.*.js` is used as is; with several, the newest is used and `UNOWASM003` is reported.
- `dotnet.native.js` and `dotnet.runtime.js` are not candidates. With `WasmFingerprintAssets=false` there is nothing to resolve.
- Leftover candidates that were ignored are logged.

## Non-goals

- Cleaning the publish directory.

## Validation

- Unit tests in `Given_DotnetJsResolver` cover: stale files newer than the current one, a mapped-but-missing file, an unrelated first manifest followed by a matching one, the project's own manifest winning over leftover manifests (including when its mapped file is missing), other manifests ordered newest first, malformed manifests, a base path in the route, no manifest, and files without a fingerprint.
- `src/Uno.Wasm.Tests.Fingerprint/test-fingerprint.sh` republishes into a dirty directory.
