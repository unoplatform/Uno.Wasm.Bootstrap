---
uid: Uno.Wasm.Bootstrap.Security
---
# Security

## CSP Support

Starting from the bootstrapper 7.0.20, the bootstrapper supports [Content Security Policy (CSP)](https://developer.mozilla.org/en-US/docs/Web/HTTP/CSP) for the web application. Specifically, the bootstrapper is compliant with the `unsafe-eval` CSP feature.

Enabling CSP support can be done in three ways:
- Adding the following block in the `.csproj`:
	```xml
	<PropertyGroup>
		<WasmShellCSPConfiguration>default-src 'self'; script-src 'self' 'wasm-unsafe-eval'</WasmShellCSPConfiguration>
	</PropertyGroup>
	```
- Adding the following meta block in the index.html, you have a custom one:
	```html
	<meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self' 'wasm-unsafe-eval'">
	```
- Providing the following header from the server:
	```
	Content-Security-Policy: default-src 'self'; script-src 'self' 'wasm-unsafe-eval'
	```

> [!IMPORTANT]
> The Uno.Wasm.Bootstrap package uses WebAssembly, it is required to provide the [`wasm-unsafe-eval`](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Security-Policy/script-src#unsafe_webassembly_execution) directive in the CSP configuration.

Enabling CSP without `unsafe-eval` implies that the application will not be able to use [Runtime.JSInvoke](xref:Uno.Wasm.Bootstrap.JSInterop), and [JSImport/JSExport](xref:Uno.Wasm.Bootstrap.JSInterop) must be used instead.

### Validation

In order to test, browsers support a _report-only_ mode which logs violations and continues.

To enable this mode, use the `Content-Security-Policy-Report-Only` header instead of `Content-Security-Policy`.

### Limitations

Enabling CSP is not compatible with [memory profiling](xref:Uno.Wasm.Bootstrap.Profiling.Memory) and [AOT profile generation](xref:Uno.Wasm.Bootstrap.Runtime.Execution).