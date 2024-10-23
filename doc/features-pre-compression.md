---
uid: UnoWasmBootstrap.Features.PreCompression
---

# Pre-Compression support

Pre-compression has two modes:

- In-place, where Brotli compressed files are placed next to original files
- Legacy, where a `web.config` file url rewriter rule is used

The parameters for the pre-compression are as follows:

- `WasmShellGenerateCompressedFiles` which can be `true` or `false`. This property is ignored when building `MonoRuntimeDebuggerEnabled` is set to `true`, and `true` by default when the `Configuration` property is set to `Release`
- `WasmShellCompressedExtension` is an item group which specifies which files to compress. By default, the `wasm`, `clr`, `js`, `json`, `css`, `html`, `dat`, `ttf`, and `txt` files are pre-compressed. More files can be added as follows:

  ```xml
    <ItemGroup>
      <WasmShellCompressedExtension Include=".db"/>
    </ItemGroup>
  ```

- `WasmShellBrotliCompressionQuality` which controls the compression quality used to pre-compress the files. Possible values are in the range from `0` to `11`. The default value is `7`.

  > [!WARNING]
  > Two-digit compression quality values (`10` and higher) are intended for extra-intensive CPU usage and can significantly slow down build time.

- `WasmShellCompressionLayoutMode` which can be set to `InPlace` or `Legacy`. If not set and for backward compatibility reasons, `Legacy` is automatically selected if a `web.config` file is detected in the layout, and contains the `_compressed_br` string.

## Support for in-place compression

This mode is to be preferred for web servers that support `accept-encoding` header file rewriting. In the case of [**Azure Static WebApps**](https://docs.microsoft.com/en-us/azure/static-web-apps/get-started-portal), if a file next to the original one is suffixed with `.br`, and the client requested for brotli compressed files, the in-place compressed file will be served.
> [!NOTE]
> To achieve the same behavior in a standalone ASP.NET Core application the [CompressedStaticFiles](https://github.com/AnderssonPeter/CompressedStaticFiles) third-party library can be used.

## Support for IIS / Azure WebApp GZip/Brotli pre-compression

The IIS compression support has too many knobs for the size of generated WebAssembly files, which
makes the serving of static files inefficient.

The Bootstrapper tooling will generate two folders `_compressed_gz` and `_compressed_br` which contain compressed versions of the main files. A set IIS rewriting rules are used to redirect the queries to the requested pre-compressed files, with a preference for Brotli.

When building an application, place [the following file](src/Uno.Wasm.SampleNet7/wwwroot/web.config) in the `wwwroot` folder to automatically enable the use of pre-compressed files.

Note that the pre-compressed files are optional, and if the rewriting rules are removed or not used (because the site is served without IIS), the original files are available at their normal locations.
