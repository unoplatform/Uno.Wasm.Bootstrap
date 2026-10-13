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

With the Uno.Sdk, `Uno.Resizetizer` generates this file from the splash screen properties. Without a background color, which is the default for WebAssembly, the loader follows the browser's light or dark theme. To set colors or a dark-theme image:

```xml
<PropertyGroup>
    <UnoSplashScreenBackgroundColor>#FFFFFF</UnoSplashScreenBackgroundColor>
    <UnoSplashScreenDarkBackgroundColor>#202020</UnoSplashScreenDarkBackgroundColor>
    <UnoSplashScreenDarkFile>Assets\Splash\splash_screen_dark.svg</UnoSplashScreenDarkFile>
    <UnoSplashScreenAccentColor>#0F6CBD</UnoSplashScreenAccentColor>
    <UnoSplashScreenDarkAccentColor>#479EF5</UnoSplashScreenDarkAccentColor>
</PropertyGroup>
```

These become `lightThemeBackgroundColor`, `darkThemeBackgroundColor`, `splashScreenImageDark`, `accentColor` and `darkThemeAccentColor` below. Without accent colors, the progress bar uses the WinUI defaults. They need versions of the Uno.Sdk and `Uno.Resizetizer` that support them.

These properties are supported in the manifest:

- `lightThemeBackgroundColor` (optional) background color used when the browser reports `prefers-color-scheme: light` (or has no preference).
- `darkThemeBackgroundColor` (optional) background color used when the browser reports `prefers-color-scheme: dark`. Theme switching is handled entirely by CSS.
- `splashScreenColor` (optional) single-theme background color, used in both themes. When `lightThemeBackgroundColor` or `darkThemeBackgroundColor` is also set, `splashScreenColor` is ignored so each theme gets its own color. When set to `transparent`, the default background color is used.
- `splashScreenImage` (optional) path or URL to the splash image shown while the application boots.
- `splashScreenImageDark` (optional) path or URL to the splash image used when the browser reports `prefers-color-scheme: dark`. When absent, `splashScreenImage` is used in both themes.
- `accentColor`, `lightThemeAccentColor`, `darkThemeAccentColor` (optional) color of the progress bar.
- `foregroundColor`, `lightThemeForegroundColor`, `darkThemeForegroundColor` (optional) color of the loader's text and icons. All of them are drawn in this one color.

Without a foreground color, the text and icons are derived from the background, as the progress track is, and the label of the Reload button from the accent color: dark on light colors and light on dark ones, keeping a contrast of at least 4.5:1 with any background or accent. With the default colors this gives the default palette. Browsers without CSS relative colors (before Chrome 119, Safari 18, Firefox 128) use the default text colors for each theme. A foreground color set by the app is used as is, so check its contrast against your background.

The manifest's colors and logo are written into `index.html` at build time, so the first paint already shows them. When a Content-Security-Policy is configured (`WasmShellCSPConfiguration`), they are applied by the bootstrapper instead, once its script has loaded the manifest.

The logo is centered in the page, at most 620×300 pixels, which is where `ExtendedSplashScreen` from Uno.Toolkit draws it, so the hand-off from the browser loader to the app's splash screen doesn't move it. On very short screens, such as phones in landscape, the logo shrinks just enough to keep the progress bar on screen, and when a message or the Reload button is shown, it moves up so they stay visible too.

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

For example, to name the phases and show the amount of data downloaded:

```xml
<PropertyGroup>
    <WasmShellLoaderStatusText>true</WasmShellLoaderStatusText>
    <WasmShellLoaderProgressFormat>size</WasmShellLoaderProgressFormat>
</PropertyGroup>
```

The loader fades out when it is dismissed: by the bootstrapper, by a call to `Uno.WebAssembly.Bootstrap.Bootstrapper.dismissLoader()`, or when the app removes the `.uno-loader` element itself, as Uno Platform does on the app's first frame.

When no Content-Security-Policy is configured, the loader's stylesheet, `uno-bootstrap.css`, is inlined in `index.html` so the loader is displayed with the first response. If the server sends a policy that blocks inline styles, the bootstrapper loads the stylesheet as a file instead. An app's own `uno-bootstrap.css` that references other files with relative URLs is linked, not inlined.

> [!IMPORTANT]
> Other stylesheets, including the app's `WasmCSS` files, are loaded without blocking the page and applied once the bootstrapper starts, so they don't delay the loader. CSS in `WasmCSS` that restyles the loader therefore only applies after the first paint, and the default loader shows briefly before it. Use the manifest properties above, or [replace the loader](#replacing-the-loader) with styles inline in `index.html`.

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
        <button class="reload" type="button" hidden>Reload</button>
    </div>
</div>
```

`data-status-text`, `data-progress-format` and `data-logo-animation` are filled in from the MSBuild properties above, so the loader is laid out correctly before the bootstrapper starts; without them it is updated once the bootstrapper runs.

`index.html` files using the earlier markup, with the `progress` element directly inside `.uno-loader`, keep a progress bar at the bottom of the page, and the build reports `UNOWA0014` as a message. Copy the loader from the markup above to get the current one, or mark your own loader as custom.

## Replacing the loader

An app can replace the loader entirely with its own markup and styles. Provide your own `index.html` (`WasmShellIndexHtmlPath`) and mark the loader element with `data-uno-loader="custom"`. Keep the `uno-loader` class: Uno Platform finds the loader through it to keep it up until the app's first frame.

```html
<head>
    <!-- ... the rest of the bootstrapper's template ... -->
    <style>
        .uno-loader { position: fixed; inset: 0; display: grid; place-items: center; background: #2E2E2E; color: #fff; transition: opacity 250ms; }
        .uno-loader.uno-leaving { opacity: 0; }
        .uno-loader .track { width: 240px; height: 4px; background: rgb(255 255 255 / 20%); }
        .uno-loader .track > i { display: block; height: 100%; background: #fff; transform-origin: left; transform: scaleX(var(--uno-loader-progress, 0)); transition: transform 300ms; }
        .uno-loader[data-phase='failed'] .track { display: none; }
    </style>
</head>
<body>
    <div id="uno-body" class="container-fluid uno-body">
        <div class="uno-loader" data-uno-loader="custom">
            <img src="./logo.svg" alt="" />
            <div class="track"><i></i></div>
        </div>
    </div>
</body>
```

With a custom loader:

- The bootstrapper doesn't inline its loader stylesheet, so nothing styles the page but your own CSS. Put the loader's styles inline in the `<head>` of `index.html`, as above: they are then part of the first paint, and survive the app replacing the content of `#uno-body` when it starts, which the exit transition needs.
- The bootstrapper never changes the loader's content: no logo or colors from the manifest, no text, and no failure UI.
- It publishes the loading state on the element:
  - `data-phase`: `connect`, `download`, `starting` or `failed`.
  - `data-state`: `ok`, `slow`, `retry`, `offline` or `failed`.
  - `--uno-loader-progress`: the download progress, from `0` to `1` (`1` once starting).
- And as events, dispatched on the element (they bubble): `uno-loader-phase` (`detail.phase`), `uno-loader-state` (`detail.state`) and `uno-loader-progress` (`detail.progress`, from 0 to 100).
- When the loader is dismissed, it gets the `uno-leaving` class and is removed once its transitions or animations have finished (at most 2 seconds), or right away if it has none.
- When startup fails, `data-phase` becomes `failed` and the loader stays: showing an error is up to the app.
