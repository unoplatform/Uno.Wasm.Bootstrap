

### Support for PWA Manifest File
A **Progressive Web App** manifest link definition can be added to the index.html file's head:
- Use the `WasmPWAManifestFile` property to set the file name
- Add a [Web App Manifest file](https://docs.microsoft.com/en-us/microsoft-edge/progressive-web-apps-chromium/get-started#step-2---create-a-web-app-manifest).
- Ensure the build action is `Content` for this file so it gets copied to the output folder. The `UnoDeploy="Package"` mode (which is the default) must be used. This file must not be put in the `wwwroot` folder.
- Create a set of icons using the [App Image Generator](https://www.pwabuilder.com/imageGenerator)

iOS's support for home screen icon is optionally set by searching for a 1024x1024 icon in the 
PWA manifest. Not providing this image will make iOS generate a scaled-down screenshot of the application.

You can validate your PWA in the [chrome audits tab](https://developers.google.com/web/updates/2017/05/devtools-release-notes#lighthouse). If your 
PWA has all the appropriate metadata, the PWA installer will prompt to install your app.

### Support for Subresource Integrity
By default, the _msbuild task_ will calculate a hash for binary files in your project and will use the [Subresource Integrity](https://www.w3.org/TR/SRI/)
to validate that the right set of files are loaded at runtime.

You can deactivate this feature by setting this property in your `.csproj` file:

```xml
<WashShellUseFileIntegrity>False</WashShellUseFileIntegrity>
