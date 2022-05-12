### Index.html content override
The msbuild property `WasmShellIndexHtmlPath` can be used to specify the path of a project-specific `index.html` file.

This file should contain the following markers, for the runtime to initialize properly: 
- `$(ADDITIONAL_CSS)`
- `$(ADDITIONAL_HEAD)`

Use the [Templates/index.html](src/Uno.Wasm.Bootstrap/Templates/index.html) file as an example.

### Support for additional JS files

Providing additional JS files is done through the inclusion of `EmbeddedResource`  msbuild item  files, in a project folder named `WasmScripts`.
Files are processed as embedded resources to allow for libraries to provide javascript files.

### Support for additional CSS files
Additional CSS files are supported through the inclusion of `EmbeddedResource`  msbuild item files, in a project folder named `WasmCSS`.

### Support for additional Content files
Additional Content files are supported through the inclusion of `Content` files. The folder structure is preserved in the output `dist` folder. There is 3 deployment modes for content files:

* `Package`: files using `UnoDeploy="Package"` mode will be deployed in the `dist\package_<hash>` folder and the folder structure will be preserved. This is the default mode for most files (see exclusions below).
* `Root`: files using `UnoDeploy="Root"` mode will be deployed directly in the `dist\` folder and the folder structure will be preserved.
* `None`: files using the `UnoDeploy="None"` mode will be ignored and won't be deployed.

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

Asset files: `dist/package_XXXX/uno-assets.txt` contains the package relative paths of the content files that were copied to the  `dist/package_XXXX` folder. It can be used to identify which assets are packaged with the application at runtime and avoid costly probing operations. Important: Will only contain files deployed in `UnoDeploy="Package"` mode.

A few files extensions are excluded (`UnoDeploy="None")`by default such as `*.a`, `*.bc`.
 `.html` files are those named `web.config` will default to `UnoDeploy="Root"`.

