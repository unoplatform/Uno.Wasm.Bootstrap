# Feature Specification: Fluent Loading Screen

**Status**: Implemented
**Input**: Reverse specification for the loading screen (`.uno-loader`) driven by `LoaderView`.

## Overview

The bootstrapper shows a themed splash screen while the .NET runtime and the app download and start. `LoaderView` (`ts/Uno/WebAssembly/LoaderView.ts`) turns bootstrapper events into a phase and a network state, exposed as the `data-phase` and `data-state` attributes that `uno-bootstrap.css` styles.

## Phases

| Phase | Meaning | Progress representation |
|-------|---------|-------------------------|
| `connect` | Waiting for the first download progress | Indeterminate |
| `download` | Resources are downloading | Determinate, 0-100 |
| `starting` | Download done, managed startup running | Indeterminate |
| `failed` | Runtime initialization or managed startup failed | Reload button shown |

## Network states

- `ok`: normal.
- `slow`: more than 15 seconds spent in `connect` or `download`.
- `retry`: the resource loader retried or stalled a download in the last 8 seconds, during `connect` or `download`.
- `offline`: `navigator.onLine` is `false`.
- `failed`: see phases.

## Requirements

- The native `<progress>` element is the accessible representation: it has a value only during `download`, and no value (indeterminate) in every other phase.
- A failure thrown during runtime initialization (including before the loader view exists, such as a failed `uno-config.js` import), during `mainInit`, or by the promise returned from `runMain` moves the loader to `failed` and keeps it visible. A loader that was leaving, or was already removed by the app, is brought back.
- The failed copy does not assume a network cause: "Could not load app" / "Reload the page to try again."
- Leaving the loader unregisters its network listeners, interval and `PerformanceObserver`.
- The `runMain` promise is not awaited, so the loader is still dismissed as before when `Main` stays running.
- The progress label is configurable through `uno_loader_progress_format` (`percent`, `size` or `none`; MSBuild `WasmShellLoaderProgressFormat`).
- Phase labels and the slow hint are shown only when `uno_loader_status_text` is `true` (MSBuild `WasmShellLoaderStatusText`, default `false`), so a normal load has no text to translate. The `retry`, `offline` and `failed` states always show their label and hint.
- Showing or hiding text never moves the logo or the bar: the status, meta and hint lines keep their height, and the Reload button sits outside the layout.
- Apps that provide their own `.uno-loader` markup without the new elements keep working.

## Edge cases

- Progress never goes backwards while the estimated total grows.
- `failed` is terminal for the page lifetime.
- A first resource that hangs before any progress callback still reaches `slow`.

## Validation

`src/Uno.Wasm.Tests.Loader` drives a published sample through three runs. It serves files only from inside the published root.

- Normal start: `download` with a percentage and no phase label, then `starting`, then removal.
- Slow start (`slow` mode, the native wasm held for 20 s): `slow` without a hint (status text is off by default), then `offline` with its label and warning icon while the browser is offline, then back to `slow`.
- Failed start (`failwasm` mode): `failed` with its label and the Reload button.

`retry` is not covered: it needs the resource loader's retry counters, which this repository does not provide yet.
