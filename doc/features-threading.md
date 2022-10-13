## Support for WebAssembly Threads

Starting from .NET 7, experimental support for [WebAssembly threads](https://github.com/WebAssembly/threads/blob/master/proposals/threads/Overview.md) has been included. This support is provided by the boostrapper 7.0 and later, for interpreter and AOT modes. The following documentation explains how to enable threading.

> [!IMPORTANT]
> Threading support is now supported in most major browsers. You can find out if your target browser supports it on the [WebAssembly roadmap](https://webassembly.org/roadmap).

### Enabling threads
Add the following to your WebAssembly project:
```xml
<PropertyGroup>
	<WasmShellEnableThreads>true</WasmShellEnableThreads>
</PropertyGroup>
```

Threading support can be detected at runtime by using the [`UNO_BOOTSTRAP_MONO_RUNTIME_FEATURES`](features-environment-variables.md) environment variable.

### Controlling the browser thread pool size

By default, the runtime pre-creates a set of 4 Web Workers available to WebAssembly threads, and the .NET Runtime will reuse the workers as threads stop. 

To change the number of threads available to the app, use the following:
```xml
<PropertyGroup>
	<WasmShellPThreadsPoolSize>8</WasmShellPThreadsPoolSize>
</PropertyGroup>
```

### Restrictions
WebAssembly Threads are using the `SharedArrayBuffer` browser feature, which is disabled in most cases for security reasons. 

To enable `SharedArrayBuffer`, your website will need to provide the following headers:

```
Cross-Origin-Embedder-Policy: require-corp
Cross-Origin-Opener-Policy: same-origin
```

You can find [more information about it on the Chrome Developer blog](https://developer.chrome.com/blog/enabling-shared-array-buffer/).
