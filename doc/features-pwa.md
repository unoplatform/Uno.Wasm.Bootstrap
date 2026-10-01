---
uid: UnoWasmBootstrap.Features.PWA
---

# Support for PWA Manifest File

A **Progressive Web App** manifest link definition can be added to the index.html file's head:

- Use the `WasmPWAManifestFile` property to set the file name
- Add a [Web App Manifest file](https://docs.microsoft.com/en-us/microsoft-edge/progressive-web-apps-chromium/get-started#step-2---create-a-web-app-manifest).
- Ensure the build action is `Content` for this file so it gets copied to the output folder. The `UnoDeploy="Package"` mode (which is the default) must be used. This file must not be put in the `wwwroot` folder.
- Create a set of icons using the [App Image Generator](https://www.pwabuilder.com/imageGenerator)

iOS's support for home screen icon is optionally set by searching for a 1024x1024 icon in the PWA manifest. Not providing this image will make iOS generate a scaled-down screenshot of the application.

You can validate your PWA in the [chrome audits tab](https://developers.google.com/web/updates/2017/05/devtools-release-notes#lighthouse). If your PWA has all the appropriate metadata, the PWA installer will prompt to install your app.

## Offline support

When a PWA manifest is set, a service worker is registered a few seconds after the app has started, so that its downloads don't compete with the app's own.

- On install, it caches the files the app needs to start. Most come from the browser's HTTP cache, since the app just downloaded them.
- Once active, it caches the remaining published files in the background, a few at a time, so the app also works offline after the first visit.
- Files that never change (the `package_<hash>` folder and the fingerprinted `_framework` files) are served from the cache first. A dropped connection then can't break a visit when the files are already cached.
- Other requests, like `index.html`, go to the network first. When the network fails or takes more than 4 seconds, the cached copy is used.
- Files the app fetches are cached as it uses them.

Large files that are not always needed, like font families, can be left out of the background caching; they are still cached when the app uses them:

```xml
<PropertyGroup>
    <WasmShellPWAPrecacheExclude>*.ttf;*.woff2</WasmShellPWAPrecacheExclude>
</PropertyGroup>
```

`*` matches within a path segment and `**` across segments.

## Support for Subresource Integrity

By default, the _msbuild task_ will calculate a hash for binary files in your project and will use the [Subresource Integrity](https://www.w3.org/TR/SRI/)
to validate that the right set of files are loaded at runtime.

You can deactivate this feature by setting this property in your `.csproj` file:

```xml
<WashShellUseFileIntegrity>False</WashShellUseFileIntegrity>
