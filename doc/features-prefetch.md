### Support for Content prefetching
The `WashShellGeneratePrefetchHeaders` controls the generation of `<link rel="prefetch" />` nodes in the index.html header.

It is disabled by default and allows for the browser to efficiently fetch the applications WebAssembly and .NET assemblies files, while the JavaScript and WebAssembly runtimes are being initialized.

This prefetching feature is particularly useful if the HTTP server supports HTTP/2.0.