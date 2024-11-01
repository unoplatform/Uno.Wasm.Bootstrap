---
uid: UnoWasmBootstrap.Features.AdditionalFiles
---

# Index.html content override

The msbuild property `WasmShellIndexHtmlPath` can be used to specify the path of a project-specific `index.html` file.

This file should contain the following markers, for the runtime to initialize properly:

- `$(ADDITIONAL_CSS)`
- `$(ADDITIONAL_HEAD)`

Use this file as an example:

- [Templates/index.html](https://github.com/unoplatform/Uno.Wasm.Bootstrap/blob/main/src/Uno.Wasm.Bootstrap/Templates/index.html) for bootstrapper 8.x.
- [Templates/index.html](https://github.com/unoplatform/Uno.Wasm.Bootstrap/blob/release/stable/7.0/src/Uno.Wasm.Bootstrap/Templates/index.html) for bootstrapper 7.x.
- [Templates/index.html](https://github.com/unoplatform/Uno.Wasm.Bootstrap/blob/release/stable/3.3/src/Uno.Wasm.Bootstrap/Templates/index.html) for bootstrapper 3.x.
- [Templates/index.html](https://github.com/unoplatform/Uno.Wasm.Bootstrap/blob/release/stable/2.1/src/Uno.Wasm.Bootstrap/Templates/index.html) for bootstrapper 2.x.

## Support for additional JS files

Providing additional JS files is done through the inclusion of `EmbeddedResource`  msbuild item  files, in a project folder named `WasmScripts`.
Files are processed as embedded resources to allow for libraries to provide javascript files.

## Support for additional CSS files

Additional CSS files are supported through the inclusion of `EmbeddedResource`  msbuild item files, in a project folder named `WasmCSS`.

## Support for additional Content files

Additional Content files are supported through the inclusion of `Content` files. The folder structure is preserved in the output `dist` folder. There is 3 deployment modes for content files:

- `Package`: files using `UnoDeploy="Package"` mode will be deployed in the `dist\package_<hash>` folder and the folder structure will be preserved. This is the default mode for most files (see exclusions below).
- `Root`: files using `UnoDeploy="Root"` mode will be deployed directly in the `dist\` folder and the folder structure will be preserved.
- `None`: files using the `UnoDeploy="None"` mode will be ignored and won't be deployed.

Exclusions:

1. Files in the `WasmScript` folder will be set as `UnoDeploy="None"` by default (they are not treat as content)

2. Files in the `wwwroot` folder will be set as `UnoDeploy="Root"` by default

3. You can manually set the _deploy mode_ in the `.csproj` in the following way:

   ```xml
   <ItemGroup>
       <!-- Manually set a file to be deployed as "root" mode -->
       <Content Include="Path\To\My\File.txt" UnoDeploy="Root" />
   
       <!-- Manually set a file to be deployed as "package" mode -- overriding the default "root" mode for wwwroot -->
       <Content Include="wwwroot\config.json" UnoDeploy="Package" />
   
       <!-- Manually set a file to be deployed as "none" mode (not deployed) -->
       <Content Include="wwwroot\output.log" UnoDeploy="None" />
   </ItemGroup>
   ```

1. A few files extensions are excluded (`UnoDeploy="None")`by default such as `*.a`, `*.o`. `.html` files are those named `web.config` will default to `UnoDeploy="Root"`.

### Asset dictionary 

The file `wwwroot/package_XXX/uno-assets.txt` contains the package relative paths of the content files that were copied to the `wwwroot` folder. 

It can be used to identify which assets are packaged with the application at runtime and avoid costly probing operations.

The files are specified in two parts:

- The files located in the `_framework` folder, which are all the assemblies used to run the app. The path in the `uno-assets.txt` file is relative to the base uri of the site.
- The files contained in `package_XXX` folder, which are the Content files specified at build time. The path in the `uno-assets.txt` file is relative to the `package_XXX` folder of the site.

> [!IMPORTANT]
> This file only contain files deployed in `UnoDeploy="Package"` mode.
