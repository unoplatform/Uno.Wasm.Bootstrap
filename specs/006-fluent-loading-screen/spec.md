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
- `retry`: the resource loader retried or stalled a download recently.
- `offline`: `navigator.onLine` is `false`.
- `failed`: see phases.

## Requirements

- The native `<progress>` element is the accessible representation: it has a value only during `download`, and no value (indeterminate) in every other phase.
- A failure thrown during runtime initialization, during `mainInit`, or by the promise returned from `runMain` moves the loader to `failed` and keeps it visible.
- The `runMain` promise is not awaited, so the loader is still dismissed as before when `Main` stays running.
- The progress label is configurable through `uno_loader_progress_format` (`percent` or `size`).
- Apps that provide their own `.uno-loader` markup without the new elements keep working.

## Edge cases

- Progress never goes backwards while the estimated total grows.
- `failed` is terminal for the page lifetime.
- A first resource that hangs before any progress callback still reaches `slow`.

## Validation

`src/Uno.Wasm.Tests.Loader` drives a published sample through a normal start and a failed start (`failwasm` mode), asserting the phases and states above. It serves files only from inside the published root.
