# Feature Specification: `WasmShellWebAppBasePath` Normalization

**Feature Branch**: `dev/mikernet/base-path-normalization`
**Created**: 2026-09-21
**Status**: Implemented
**Input**: Fix for generated references being corrupted when `WasmShellWebAppBasePath` is written without its leading or trailing slash.

## Overview

`WasmShellWebAppBasePath` is the site path the application is hosted under. The bootstrapper prefixes it onto every absolute reference it generates: the script and stylesheet tags in `index.html`, `config.uno_app_base`, the offline file list, the manifest link, the service worker import and registration scope, and the `UNO_BOOTSTRAP_WEBAPP_BASE_PATH` environment variable. All of those concatenate the value directly, so the value had to be written exactly as `/app/`.

This specification normalizes the value once, when the build task reads its properties, so that `app`, `app/`, `/app`, and `/app/` are equivalent.

## Problem Statement

- `/app` without a trailing slash produced `/apppackage_<hash>/...` and `/appuno-config.js`, so the application failed to load.
- `app/` without a leading slash was resolved by the browser relative to the current document, so it only worked on a page without path segments and broke on every deep link.
- An empty value made the `index.html` rewrite match every quote character in the template.

## Functional Requirements

**FR-1**: `ShellTask` SHALL normalize `WebAppBasePath` during `ParseProperties()`, before any generated output uses it.

**FR-2**: An empty or whitespace value SHALL normalize to `./`, the bootstrapper's document-relative default.

**FR-3**: A value starting with `.` SHALL stay relative; only a missing trailing slash is added.

**FR-4**: A value containing `://` SHALL stay as written; only a missing trailing slash is added.

**FR-5**: Any other value SHALL receive a leading slash when missing and a trailing slash when missing. Backslashes are converted to slashes and surrounding whitespace is removed.

**FR-6**: Every consumer of the base path (index.html rewrite, `uno-config.js`, service worker, PWA content, environment variables) SHALL observe the normalized value.

## Non-Goals

- Validating that an absolute URL base path is usable. Cross-origin hosting has other constraints, such as service worker registration, that are outside the scope of normalization.
- Changing how relative references inside `index.html` are relocated into the package folder.

## Edge Cases

- `.` and `..` normalize to `./` and `../`.
- `\app` is treated as `/app/`, since MSBuild property values on Windows often carry backslashes.
- Uno.Sdk sets the property to `/` when it is empty; the normalization does not change that value.

## Validation

- `src/Uno.Wasm.Bootstrap.UnitTests/Given_WebAppBasePathHelper.cs` covers every rule above.
- Building a sample with `-p:WasmShellWebAppBasePath=app` produces `index.html`, `uno-config.js` and `service-worker.js` references identical to a build with `/app/`.
