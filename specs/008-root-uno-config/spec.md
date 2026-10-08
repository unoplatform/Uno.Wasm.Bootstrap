# Feature Specification: Root-Level `uno-config.js`

**Feature Branch**: `dev/mikernet/uno-config-cache`
**Created**: 2026-09-21
**Status**: Implemented
**Input**: Fix for the stale boot configuration served from the fingerprinted package folder after a publish that only changes managed code.

## Overview

The bootstrapper emits the application's static content into a `package_<hash>/` folder whose name is a hash of that content, so hosts can cache everything inside it as immutable. `uno-config.js` is the boot configuration consumed by the bootstrapper, the service worker, and WebWorker scripts. Its content is not part of the package hash and it is rewritten after the hash is computed, so it must not live inside the hashed folder.

This specification moves `uno-config.js` to the root of `wwwroot`, next to `index.html`, for every shell mode, and updates every consumer that locates it.

## Problem Statement

Two mechanisms make `uno-config.js` change independently of the package hash:

1. `ShellTask.GeneratePackageFolder()` hashes the `StaticWebContent` items, and `GenerateConfig()` writes `uno-config.js` afterwards. The file is never an input to the hash.
2. `_UnoUpdateDotnetJsFingerprintPublishOutput` rewrites the `dotnet.<fingerprint>.js` reference inside the published `uno-config.js`, because the fingerprint produced by publish differs from the one known at build time.

With the file inside `package_<hash>/`, a publish that only changes C# or XAML keeps the folder name and changes the file. Hosts that cache `package_*` as immutable, as the Uno Platform template's `staticwebapp.config.json` does, then serve the previous `uno-config.js` to returning visitors. The stale file points at a `dotnet.<old>.js` that no longer exists or at the previous runtime, so the app fails to boot or runs the previous build until the package hash happens to change.

## Functional Requirements

**FR-1**: `ShellTask.GenerateConfig()` SHALL emit `uno-config.js` with the static web asset link `wwwroot/uno-config.js`, in every shell mode.

**FR-2**: The main bootstrapper (`uno-bootstrap.js`, deployed inside `package_<hash>/`) SHALL import the configuration from `../uno-config.js`, relative to its own module URL, so that the resolution is correct when the application is hosted from a site root, from a sub-folder, or through `embedded.js`.

**FR-3**: The service worker SHALL import the configuration from `$(REMOTE_WEBAPP_PATH)uno-config.js`, and the offline file list SHALL reference `uno-config.js` at the web app base path. The bootstrapper SHALL register the service worker with `updateViaCache: 'none'`, so an updated worker never imports a previous deployment's `uno-config.js` from the HTTP cache.

**FR-4**: In WebWorker shell mode, `worker.js` SHALL fetch `uno-config.js` from its own folder. The `__unoWorkerPackagePath` global is no longer emitted.

**FR-5**: `_UnoUpdateDotnetJsFingerprintPublishOutput` SHALL update `wwwroot/uno-config.js` in the publish output. The former lookup under `$(WasmShellOutputPackagePath)` is removed.

**FR-6**: The version checker SHALL resolve the configuration of a page that references `<package>/uno-bootstrap.js` by probing `uno-config.js` one folder above the bootstrapper first, then next to it for older layouts. The `embedded.js` fallback SHALL probe the site root before `<package>/uno-config.js`. A probe only matches when the response contains an Uno configuration (`config.uno_`), because single-page-app hosts answer unknown paths with `index.html`; a network failure on a probe moves on to the next candidate.

**FR-6a**: The `Uno.Wasm.Bootstrap.Server` publish-time fingerprint update SHALL update `wwwroot/uno-config.js`. When that file is absent (an app built with an older bootstrapper), it SHALL fall back to `wwwroot/package_*/uno-config.js`, never to the WebWorker copies nested deeper. When several `package_*/uno-config.js` match, none is updated and a warning asks to clean the publish directory.

**FR-7**: `config.uno_app_base`, the `UNO_BOOTSTRAP_APP_BASE` environment variable, and every other package-relative path in the configuration SHALL be unchanged.

## Non-Goals

- Re-computing the package hash after the publish-time fingerprint update. Renaming the folder would require rewriting every reference baked into `index.html`, `embedded.js`, and the service worker.
- Changing how the WebWorker `_framework/` folder is published into the host's package folder. That copy happens after the host hash is computed as well and is tracked separately.
- Changing the shipped hosting configurations. They live in the Uno Platform templates: `staticwebapp.config.json` caches the root files for an hour (`must-revalidate, max-age=3600`) and `web.config` sets no cache policy. Both need an explicit always-revalidate policy for the root files for this layout to be fully effective; the documentation describes the expected policy.

## Edge Cases

- **Sub-folder hosting**: the bootstrapper resolves `../uno-config.js` against its module URL, so `https://host/app/package_x/uno-bootstrap.js` loads `https://host/app/uno-config.js`.
- **Embedded mode**: `embedded.js` sets `<base>` to the package folder and imports `package_x/uno-bootstrap.js`; dynamic `import()` resolves relative to the module URL, not the document base, so the config is still found next to `embedded.js`.
- **Custom `index.html`**: the template only references `./uno-bootstrap.js`; pages that referenced `./uno-config.js` directly must be updated to the root path.
- **Pre-compressed siblings**: the publish-time update still deletes `uno-config.js.gz` and `uno-config.js.br` after rewriting the file.
- **Hosts that cache root files**: a host that caches `uno-config.js` reintroduces the problem until the cached copy expires, and `index.html` and `uno-config.js` can then come from different deployments. Without an explicit `Cache-Control` header, browsers apply heuristic caching and some CDNs cache `.js` files by extension. The documentation asks for `no-cache` on the root files.

## Validation

- `src/Uno.Wasm.Tests.Fingerprint/test-fingerprint.sh` asserts that the published `uno-config.js` is at the `wwwroot` root and absent from every `package_*` folder.
- `src/Uno.Wasm.VersionChecker.UnitTests/Given_VersionCheckService.cs` covers the root layout for both the `uno-bootstrap.js` and the `embedded.js` discovery paths, alongside the existing sibling-layout tests.
- CI validation scripts (`validate-boot-config.sh`, `validate-dotnetjs-fingerprint.sh`, `test-webgl-4gb.sh`, `test-webworker.sh`) locate the configuration at the root. The two `validate-*` scripts check only the root file, so a stale copy under `package_*` cannot pass them.
- Manual: publish, load the app, change only managed code, publish again, reload with the package folder cached as immutable. The app boots the new build.
