# Feature Specification: Startup Preload Links

**Status**: Implemented

## Overview

Publishing adds `<link rel="preload">` and `<link rel="modulepreload">` hints to the published `index.html` so the browser fetches the startup chain in parallel instead of one round-trip at a time.

## Behavior

- The hints are inserted before `</head>`, between `<!-- uno-preload-links -->` and `<!-- /uno-preload-links -->`.
- Covered files:
  - `modulepreload`: `uno-config.js`, `dotnet.js`, `jsModuleRuntime` and `jsModuleNative` modules.
  - `preload as="fetch" type="application/wasm" crossorigin="anonymous"`: `wasmNative` files, matching the runtime's cors-mode fetch. No `integrity` attribute: Chrome ignores it on fetch preloads and warns.
  - `preload as="script"`: the `config.uno_dependencies` entries loaded by `require.js`.
- Supported resource shapes in the boot config embedded in `dotnet.js`: array of `{ name }` (.NET 10+) and the legacy dictionary keyed by file name, for every resource group.
- URLs are derived from the `uno-bootstrap.js` script tag in `index.html`, so they follow the base path. The `uno-config.js` URL follows where the file was published: next to `index.html` when it is there, otherwise in the `package_<hash>` folder.
- `dotnet.js` is `dotnet.<fingerprint>.js` when fingerprinted, plain `dotnet.js` otherwise. Generation does not depend on fingerprinting.

## Opt-out

`WasmShellGeneratePreloadLinks=false` disables the feature. It is the only switch.

## Republishing

A block from an earlier run is replaced, never duplicated. Pre-compressed `index.html.gz`/`.br` siblings are deleted since they hold the old content. The target runs after `Publish` and again after the nested AOT publish.

## Failure modes

The task never fails the publish. It skips silently when `index.html`, `dotnet.js` or `uno-config.js` is missing, and logs a message when `index.html` has no `uno-bootstrap.js` script tag. A `dotnet.js` without an embedded boot config gets only the entry modules.

## Validation

- Unit tests: `Given_PreloadLinks` (both resource shapes, markers, idempotence).
- `Uno.Wasm.Tests.Fingerprint/test-fingerprint.sh` Test 11: hints include the current `dotnet.js` and every hint points at a published file.
