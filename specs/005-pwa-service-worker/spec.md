# Feature Specification: PWA Service Worker Caching

**Created**: 2026-10-02
**Status**: In Progress

## Overview

When a PWA manifest is set, the bootstrapper registers a service worker (`src/Uno.Wasm.Bootstrap/Embedded/service-worker.js`) so the app starts and runs offline, and survives a flaky network. User-facing documentation is in `doc/features-pwa.md`.

## Behavior

### Cache strategies

- **Immutable files** are served cache-first, and fetched from the network only on a miss. Immutable means everything under the `package_<hash>` folder and the fingerprinted `_framework` files (`name.<10 chars>.ext`).
- **Everything else** (`index.html`, `uno-config.js`, ...) is served network-first. The network gets 4 seconds to answer, including reading the whole body. On failure, timeout or a truncated body, the cached copy is used (ignoring the query string); if there is none, the request is retried `UNO_BOOTSTRAP_FETCH_RETRIES` times (default 1) with exponential backoff, then answered with a `503`.
- Only same-origin `GET` requests are handled. Successful responses are cached as the app uses them. Those cache writes are kept alive with `event.waitUntil`, so the worker isn't stopped before they complete.
- When `UNO_BOOTSTRAP_DEBUGGER_ENABLED` is `True`, the worker never caches anything.

### Install and background precache

- On install, the worker caches only the files needed to start: the app root, `uno-config.js`, `dotnet.js`, the package `.js`/`.css` files and the assemblies/runtime files listed in the boot config. A boot config failure is logged and does not fail the install.
- The bootstrapper sends `uno-precache` once the app is running. The worker then caches the remaining `offline_files`, 3 at a time, skipping files already cached and files matching an exclusion.
- On activate, caches from other versions are deleted and the worker claims its clients.

### Exclusions (`WasmShellPWAPrecacheExclude`)

A `;`-separated list of globs matched against the end of the file path, case-insensitively. `*` matches within a path segment, `**` across segments. Excluded files are not background-precached but are still cached when the app fetches them.

## Validation

`src/Uno.Wasm.Tests.ServiceWorker` publishes the RayTracer sample, serves it with a switchable network and checks, with Puppeteer:

- The worker takes control and the background precache settles.
- Excluded files are not precached, and every other offline file is cached.
- An excluded file is cached when fetched, then served with the network offline.
- The app reloads with the network offline, and on a flaky network where each file's first request is reset mid-body.
