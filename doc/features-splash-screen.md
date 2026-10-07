---
uid: UnoWasmBootstrap.Features.SplashScreen
---

# Splash screen customization

The default configuration for the bootstrapper is to show the Uno Platform logo. This can be changed, along with the background color and progress bar color by doing the following:

- Create an AppManifest.js file in the `WasmScripts` folder
- Set its build action to `EmbeddedResource`
- Add the following content:

  ```javascript
  var UnoAppManifest = {
      splashScreenImage: "https://microsoft.github.io/microsoft-ui-xaml/img/winui-logo.png",
      splashScreenColor: "#00f",
      accentColor: "#f00",
      displayName: "WinUI App"
  }
  ```

These properties are supported in the manifest:

- `lightThemeBackgroundColor` (optional) background color used when the browser reports `prefers-color-scheme: light` (or has no preference). Typically emitted by `Uno.Resizetizer` from the `BackgroundColor` metadata on `UnoSplashScreen`.
- `darkThemeBackgroundColor` (optional) background color used when the browser reports `prefers-color-scheme: dark`. Typically emitted by `Uno.Resizetizer` from the `DarkBackgroundColor` metadata on `UnoSplashScreen`. Applied via the existing `@media (prefers-color-scheme: dark)` rule on `.uno-loader`, so theme switching is handled entirely by CSS.
- `splashScreenColor` (optional, legacy) single-theme background color. Applied inline and therefore overrides any `@media`-driven theme switching. When `lightThemeBackgroundColor` or `darkThemeBackgroundColor` is also set, `splashScreenColor` is ignored so theme selection flows through CSS. When set to `transparent`, the default browser background color is used.
- `splashScreenImage` (optional) path or URL to the splash image shown while the application boots.
- `splashScreenImageDark` (optional) path or URL to the splash image used when the browser reports `prefers-color-scheme: dark`. When absent, `splashScreenImage` is used in both themes. Typically emitted by `Uno.Resizetizer` from the `DarkImage` metadata on `UnoSplashScreen`. The theme is snapshotted at splash-render time via `window.matchMedia('(prefers-color-scheme: dark)')`; toggling the OS theme mid-load does not re-render the already-visible splash.

## Loading progress

By default the loader shows only the logo and a progress bar. The bar fills as the app downloads, and sweeps while the app connects and again while it starts after the downloads. A normal load shows no text, so there is nothing to translate. Problems are always described:

- **Connection interrupted** while downloads are being retried, and **Device offline** when the browser loses its connection.
- **Could not load app** with a **Reload** button if the app fails to start.

These MSBuild properties change what is shown:

| Property | Values | Default |
|---|---|---|
| `WasmShellLoaderStatusText` | `true` names each phase (**Getting ready…**, **Downloading app**, **Starting…**) and adds a hint when the connection is slow. | `false` |
| `WasmShellLoaderProgressFormat` | `percent` or `size` (the megabytes downloaded) shows the progress below the bar; `none` hides it. | `none` |
| `WasmShellLoaderLogoAnimation` | `false` keeps the logo still instead of gently scaling it up and down. It still fades in and out. | `true` |

The loader fades out when the bootstrapper dismisses it. Apps that remove the loader themselves should call `Uno.WebAssembly.Bootstrap.Bootstrapper.dismissLoader()` instead: removing the `.uno-loader` element directly hides it at once, without the transition.

For example, to name the phases and show the amount of data downloaded:

```xml
<PropertyGroup>
    <WasmShellLoaderStatusText>true</WasmShellLoaderStatusText>
    <WasmShellLoaderProgressFormat>size</WasmShellLoaderProgressFormat>
</PropertyGroup>
```

When no Content-Security-Policy is configured (`WasmShellCSPConfiguration`), the loader's stylesheet, `uno-bootstrap.css`, is inlined in `index.html` so the loader is displayed with the first response. Other stylesheets are loaded without blocking the page and applied as soon as the bootstrapper starts.

### Customizing index.html

The loader uses this markup, all of which is optional apart from the `.uno-loader` element:

```html
<div class="uno-loader" loading-position="bottom" loading-alert="none"
     data-status-text="off" data-progress-format="none" data-logo-animation="on" data-phase="connect">
    <img class="logo" src="" alt="" />
    <div class="bar">
        <progress max="100" aria-label="Loading"></progress>
        <i class="fill"></i>
        <i class="sweep"></i>
    </div>
    <div class="info">
        <div class="status" role="status" aria-live="polite">
            <span class="alert" aria-hidden="true"></span>
            <span class="label"></span>
        </div>
        <div class="meta"></div>
        <div class="hint" role="status" aria-live="polite"></div>
        <button class="reload" type="button">Reload</button>
    </div>
</div>
```

`data-status-text`, `data-progress-format` and `data-logo-animation` are filled in from the MSBuild properties above, so the loader is laid out correctly before the bootstrapper starts; without them it is updated once the bootstrapper runs.

`index.html` files using the earlier markup, with the `progress` element directly inside `.uno-loader`, keep a progress bar at the bottom of the page.
