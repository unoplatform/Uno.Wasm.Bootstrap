# Feature Specification: PWA Manifest `start_url` and `scope` Defaults

**Feature Branch**: `dev/mikernet/pwa-manifest-defaults`
**Created**: 2026-09-21
**Status**: Implemented
**Input**: Enhancement so that a PWA manifest without `start_url` or `scope` follows `WasmShellWebAppBasePath`.

## Overview

The bootstrapper already rewrites the `icons` entries of the PWA manifest to the package folder and prefixes generated references with `WasmShellWebAppBasePath`. The two manifest members that describe where the application lives, `start_url` (the URL opened when the installed app is launched) and `scope` (the URL prefix the browser treats as part of the app), were left to the author. The Uno templates hard-coded them to `/index.html` and `/`, which repeats what the base path already says and is wrong as soon as the application is hosted under a sub-folder.

This specification makes the bootstrapper fill both members from the base path when the manifest does not define them.

## Functional Requirements

**FR-1**: When the manifest has no `start_url`, `GeneratePWAContent()` SHALL set it to the normalized `WasmShellWebAppBasePath`.

**FR-2**: When the manifest has no `scope`, `GeneratePWAContent()` SHALL set it to the normalized `WasmShellWebAppBasePath`.

**FR-3**: Values present in the manifest SHALL be kept as written, including an explicit `start_url` that differs from the base path.

**FR-4**: The defaults SHALL be applied before the manifest is written to the intermediate output, so that the published manifest and the `index.html` head reflect them.

## Non-Goals

- Validating that an explicit `start_url` lies within `scope`.
- Changing the icon relocation or the `apple-touch-icon` generation.

## Edge Cases

- With the bootstrapper default `./`, both members become `./`, which the browser resolves relative to the manifest at the root of the deployment.
- A manifest that defines `start_url` but not `scope` gets only `scope` defaulted, and the other way around.
- Uno.Sdk projects get `/` for both, which matches the previous template values in effect (`/index.html` launched the same page, but as a file path that could conflict with client-side routing).

## Validation

- `src/Uno.Wasm.Bootstrap.UnitTests/Given_PwaManifestHelper.cs` covers the default, explicit, and partial cases.
- Building `Uno.Wasm.Tests.Fingerprint` with a manifest that omits both members and `-p:WasmShellWebAppBasePath=/app/` produces a published manifest with `"start_url": "/app/"` and `"scope": "/app/"`.
