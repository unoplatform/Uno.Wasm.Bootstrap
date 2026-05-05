/// <reference path="Uno/WebAssembly/WorkerBootstrapper.ts"/>

// Only auto-bootstrap when running inside a Worker context. The compiled
// `uno-worker-bootstrap.js` is embedded into the main app's WasmScripts
// dependencies; if it executed on the main thread it would attempt to fetch
// `uno-config.js` from the page root (404) and post a misleading
// `Worker .NET runtime initialization failed` error to the console.
// `importScripts` is defined only on WorkerGlobalScope.
if (typeof (<any>self).importScripts === "function") {
	Uno.WebAssembly.Bootstrap.WorkerBootstrapper.bootstrap();
}
