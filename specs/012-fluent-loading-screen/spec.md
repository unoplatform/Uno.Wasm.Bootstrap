# Feature Specification: Fluent Loading Screen

**Status**: Implemented
**Input**: Specification for the loading screen (`.uno-loader`) driven by `LoaderView`, including app-provided loaders.

## Overview

The bootstrapper shows a themed splash screen while the .NET runtime and the app download and start. `LoaderView` (`ts/Uno/WebAssembly/LoaderView.ts`) turns bootstrapper events into a phase and a network state, exposed as the `data-phase` and `data-state` attributes that `uno-bootstrap.css` styles. Apps can replace the loader entirely (`data-uno-loader="custom"`); the bootstrapper then only publishes the state.

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

### Layout

- The logo is centred in the viewport at all sizes, at most `min(90vw, 620px)` wide and `min(90vh, 300px)` tall: where Uno.Toolkit's `ExtendedSplashScreen` draws it, so the hand-off doesn't move it. The bar and text hang below the logo (gap `clamp(32px, 8vh, 96px)`) and never move it.
- On screens too short for both, the logo shrinks to `100vh - 2 × (gap + 24px)` so the bar stays on screen.
- The native `<progress>` element is the accessible representation: it has a value only during `download`, and no value (indeterminate) in every other phase.
- The progress label is configurable through `uno_loader_progress_format` (`none` by default, `percent` or `size`; MSBuild `WasmShellLoaderProgressFormat`). With the defaults the loader is just the logo and the bar.
- Phase labels and the slow hint are shown only when `uno_loader_status_text` is `true` (MSBuild `WasmShellLoaderStatusText`, default `false`), so a normal load has no text to translate. The `retry`, `offline` and `failed` states always show their label and hint.
- Showing or hiding text never moves the logo or the bar. With status text on, the status, meta and hint lines keep their height and the Reload button sits outside the layout. With it off, only the progress value takes space (8 px below the bar; none at all with format `none`); problem text and the Reload button overflow below it.
- The logo breathes (scales to 1.04 and back) unless `uno_loader_logo_animation` is `false` (MSBuild `WasmShellLoaderLogoAnimation`, default `true`); its entrance and exit stay.

### First paint

- `uno-bootstrap.css` is inlined in `index.html` when no Content-Security-Policy is set. Other stylesheets load as `media="print"` and are enabled by the bootstrapper, so a slow stylesheet never delays the loader or the scripts.
- `data-status-text`, `data-progress-format` and `data-logo-animation` are written into `index.html` at build time.
- The project's `AppManifest.js` is parsed at build time and its colors (`lightThemeBackgroundColor`, `darkThemeBackgroundColor`, `splashScreenColor`, `accentColor`, `lightThemeAccentColor`, `darkThemeAccentColor`) and logo (`splashScreenImage`, `splashScreenImageDark` as a `<picture>` source for `prefers-color-scheme: dark`) are written into the loader markup, marked `data-manifest="baked"`; the bootstrapper then doesn't apply the manifest again. Per-theme background colors take precedence over `splashScreenColor`. Colors that aren't plain CSS colors and image URLs with a scheme other than `http(s)` are ignored. Under a Content-Security-Policy, or without a manifest, the bootstrapper applies the manifest at runtime as before.

### Lifecycle

- A failure thrown during runtime initialization (including before the loader view exists), during `mainInit`, or by the promise returned from `runMain` moves the loader to `failed` and keeps it visible. A loader that was leaving, or that the bootstrapper had removed, is brought back.
- When the app removes the loader element itself (Uno Platform does on its first frame), the loader is put back before the next paint and fades out like any dismissal. A later failure then doesn't bring it back over the running app, when the loader was marked `uno-keep-loader` (managed by the app).
- Leaving the loader unregisters its network listeners, interval, `PerformanceObserver` and removal watcher.
- The `runMain` promise is not awaited, so the loader is still dismissed as before when `Main` stays running.
- The failed copy does not assume a network cause: "Could not load app" / "Reload the page to try again."

### App-provided loader

- A `.uno-loader` element with `data-uno-loader="custom"` is the app's: the build doesn't inline `uno-bootstrap.css` or bake the manifest, and the bootstrapper doesn't change its content or attributes beyond `data-phase`, `data-state` and the `--uno-loader-progress` custom property (0 to 1).
- `uno-loader-phase`, `uno-loader-state` and `uno-loader-progress` events (detail `phase`, `state`, `progress` 0-100) are dispatched on the loader and bubble, for built-in and custom loaders alike.
- Dismissing a custom loader adds `uno-leaving` and removes it once its transitions or animations end (capped at 2 s), or immediately without any.
- On failure a custom loader only gets `data-phase="failed"`; it isn't re-inserted and no UI is injected.
- The `uno-loader` class stays required: Uno Platform keeps the loader up through it (`uno-keep-loader`, `uno-persistent-loader`).

### Older index.html files

- Loader markup without the `.bar` element (the earlier template) keeps a progress bar at the bottom of the page, and the build reports warning `UNOWA0014` pointing to the current template or to `data-uno-loader="custom"`.

## Non-goals

- Re-rendering the loader when the OS theme changes mid-load (the `<picture>` source and CSS follow it, the runtime fallback doesn't).
- Generating the manifest keys: that's `Uno.Resizetizer` (`BackgroundColor`, `DarkBackgroundColor` and `DarkFile` on `UnoSplashScreen`) and the Uno.Sdk (`UnoSplashScreenBackgroundColor`, `UnoSplashScreenDarkBackgroundColor`, `UnoSplashScreenDarkFile`; no default color on WebAssembly, so the loader follows the browser theme).

## Edge cases

- Progress never goes backwards while the estimated total grows.
- `failed` is terminal for the page lifetime.
- A first resource that hangs before any progress callback still reaches `slow`.
- `prefers-reduced-motion: reduce` removes the loader without the fade.

## Validation

Unit tests (`Given_LoaderMarkup`): manifest parsing (Resizetizer output, quoted keys, single quotes, comments, garbage), baking colors and logos, precedence, rejected colors and script URLs, attribute encoding, custom loaders left untouched, legacy markup detection.

`src/Uno.Wasm.Tests.Loader` drives the published RayTracer sample (which has an `AppManifest.js` with per-theme colors). It serves files only from inside the published root.

- Normal start: the stylesheet is inlined and the manifest baked (light background on the first paint); `download` with no text and nothing taking space below the bar; the logo centred in the viewport; then `starting`, then removal. A dark-scheme page gets the dark background.
- Uno Platform hand-off (`keep` mode): the loader stays while marked `uno-keep-loader`; removing the element directly makes it fade out, then it's removed.
- Custom loader (`custom` mode): content and attributes untouched, phase events received, and the loader's own exit transition runs before removal.
- Slow start (`slow` mode, the native wasm held for 20 s): `slow` without a hint, then `offline` with its label and warning icon while the browser is offline, then back to `slow`.
- Failed start (`failwasm` mode): `failed` with its label and the Reload button.

`retry` is not covered: it needs the resource loader's retry counters, which this repository does not provide yet.
