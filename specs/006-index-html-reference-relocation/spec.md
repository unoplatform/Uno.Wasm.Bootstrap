# Feature Specification: `index.html` Reference Relocation

**Feature Branch**: `dev/mikernet/index-html-relocation`
**Created**: 2026-09-21
**Status**: Implemented
**Input**: Fix for absolute references in a custom `index.html` being rewritten into the package folder.

## Overview

The bootstrapper's `index.html` template references its support files relatively (`./require.js`, `./uno-bootstrap.js`), while the build deploys those files into the hashed `package_<hash>/` folder. `GenerateIndexHtml()` therefore rewrites references in the page so they point into the package folder, prefixed with `WasmShellWebAppBasePath`.

This specification restricts that rewrite to references that actually target a file deployed to the package folder, so that other references in a custom page are left as written.

## Problem Statement

The rewrite was a blind prefix replacement: every double-quoted attribute value starting with `./` or with the web app base path was prefixed with the package folder. With the `/` base path that Uno.Sdk applies by default, every site-absolute reference in a custom `index.html` was affected: `href="/favicon.ico"` became `/package_<hash>/favicon.ico`, `fetch("/api/status")` became `/package_<hash>/api/status`, and `href="/"` became `/package_<hash>/`. There was no way to write an absolute reference to a root-level or external resource from a custom page.

## Functional Requirements

**FR-1**: A double-quoted reference of the form `"./<path>"` or `"<base path><path>"` SHALL be rewritten to `"<base path><package folder>/<path>"` only when `<path>` is the package-relative path of a file deployed to the package folder.

**FR-2**: A query string or fragment following `<path>` SHALL be preserved.

**FR-3**: Path comparison SHALL be case-insensitive and SHALL treat backslashes in deployment links as slashes.

**FR-4**: Any other reference SHALL be left unchanged, including site-absolute paths to resources outside the package folder, paths under the base path that are not package files, external URLs, and the bare base path itself.

**FR-5**: The `$(WEB_MANIFEST)` and `$(ADDITIONAL_CSS)` placeholders keep their existing behavior: the manifest link resolves to the root manifest, and additional stylesheets are relocated because they are package files.

**FR-6**: The `apple-touch-icon` link generated from the PWA manifest SHALL be emitted with the icon's package-folder path directly, using the same relocation as the manifest's `icons` entries, instead of relying on the page rewrite.

## Non-Goals

- Rewriting single-quoted or unquoted attribute values. The previous implementation only handled double quotes, and the template uses double quotes.
- Validating that references outside the package folder resolve at runtime.

## Edge Cases

- With the `./` base path, `./` is the only prefix considered, as before.
- A reference to a file that exists at the root of `wwwroot` but not in the package folder is not rewritten.
- A reference whose path matches a package file but carries a different case is rewritten with the case as written in the page.

## Validation

- `src/Uno.Wasm.Bootstrap.UnitTests/Given_IndexHtmlHelper.cs` covers relocation of package files under `./`, `/` and `/app/`, preservation of query strings and fragments, and the untouched cases from the problem statement.
- Building `Uno.Wasm.Tests.Fingerprint` with a custom `index.html` containing root-absolute and external references produces a page where only the bootstrapper's own references point into the package folder.
- The default template produces byte-identical output before and after the change.
