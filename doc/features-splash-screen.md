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

Below the logo, the loader shows a progress bar and what is happening:

- **Getting ready**, then **Downloading app** with the progress as a percentage, then **Starting…** while the app initializes after the downloads.
- When the download takes long, when the browser goes offline, or when downloads are being retried, a short explanation is shown.
- If the app fails to start, the loader says so and offers a **Reload** button.

The loader fades out when the bootstrapper dismisses it. Apps that remove the loader themselves should call `Uno.WebAssembly.Bootstrap.Bootstrapper.dismissLoader()` instead: removing the `.uno-loader` element directly hides it at once, without the transition.

To show the amount of data downloaded instead of a percentage:

```xml
<PropertyGroup>
    <WasmShellLoaderProgressFormat>size</WasmShellLoaderProgressFormat>
</PropertyGroup>
```

When no Content-Security-Policy is configured (`WasmShellCSPConfiguration`), the loader's stylesheet, `uno-bootstrap.css`, is inlined in `index.html` so the loader is displayed with the first response. Other stylesheets are loaded without blocking the page and applied as soon as the bootstrapper starts.

### Customizing index.html

The loader uses this markup, all of which is optional apart from the `.uno-loader` element:

```html
<div class="uno-loader" loading-position="bottom" loading-alert="none" data-phase="connect">
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

`index.html` files using the earlier markup, with the `progress` element directly inside `.uno-loader`, keep a progress bar at the bottom of the page.
