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
