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

- `lightThemeBackgroundColor` (optional) to change the light theme background color
- `darkThemeBackgroundColor` (optional)to change the dark theme background color
- `splashScreenColor` to change the background color regardless of the theme. When set to `transparent`, `lightThemeBackgroundColor` and `darkThemeBackgroundColor` will be used, otherwise the default browser background color will be used.
