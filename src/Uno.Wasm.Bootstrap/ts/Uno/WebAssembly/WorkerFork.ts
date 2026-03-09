namespace Uno.WebAssembly.Bootstrap {

	export interface WorkerForkOptions {
		args?: string[];
		onMessage?: (data: any) => void;
		onError?: (error: any) => void;
	}

	export interface WorkerHandle {
		postMessage(data: any): void;
		terminate(): void;
		ready: Promise<void>;
	}

	export class WorkerFork {

		public static forkToWorker(options?: WorkerForkOptions): WorkerHandle {

			const unoConfig: UnoConfig = (<any>globalThis).config;
			if (!unoConfig || !unoConfig.uno_worker_fork) {
				throw new Error(
					"WorkerFork: The Worker Fork feature is not enabled. " +
					"Set <WasmShellEnableWorkerFork>true</WasmShellEnableWorkerFork> in your project file."
				);
			}

			const opts = options || {};
			const args = opts.args || [];
			const onMessage = opts.onMessage || (() => { });
			const onError = opts.onError || ((e: any) => console.error("WorkerFork error:", e));

			const wasmModule = (<any>globalThis).__unoWasmModule;
			if (!wasmModule) {
				throw new Error(
					"WorkerFork: globalThis.__unoWasmModule is not available. " +
					"Ensure the .NET runtime has fully initialized before calling forkToWorker()."
				);
			}

			// Resolve absolute URLs from the main thread since blob workers have opaque origins.
			// _framework/ is at the document root, not under the app base package directory.
			const docBase = document.baseURI;
			const appBaseUrl = new URL(unoConfig.uno_app_base + "/", docBase).href;
			const frameworkBaseUrl = new URL("_framework/", docBase).href;
			const dotnetJsUrl = frameworkBaseUrl + unoConfig.dotnet_js_filename;

			// Build environment variables: copy from config and add worker flag.
			const envVars: { [key: string]: string } = {};
			if (unoConfig.environmentVariables) {
				for (const key in unoConfig.environmentVariables) {
					envVars[key] = unoConfig.environmentVariables[key];
				}
			}
			envVars["UNO_BOOTSTRAP_IS_WORKER"] = "true";

			const workerScript = WorkerFork.buildWorkerScript();
			const blob = new Blob([workerScript], { type: "application/javascript" });
			const blobUrl = URL.createObjectURL(blob);

			const worker = new Worker(blobUrl);

			let readyResolve: () => void;
			let readyReject: (err: any) => void;
			const readyPromise = new Promise<void>((resolve, reject) => {
				readyResolve = resolve;
				readyReject = reject;
			});

			worker.onmessage = (e: MessageEvent) => {
				const msg = e.data;
				if (!msg || !msg.type) return;

				switch (msg.type) {
					case "uno:worker:ready":
						readyResolve();
						break;
					case "uno:worker:error":
						const err = new Error(msg.error || "Unknown worker error");
						readyReject(err);
						onError(err);
						break;
					case "uno:worker:message":
						onMessage(msg.payload);
						break;
					case "uno:worker:log":
						// Relay worker console output to the main thread console.
						const level = msg.level === "error" ? "error" : msg.level === "warn" ? "warn" : "log";
						(console as any)[level]("[Worker] " + msg.text);
						break;
				}
			};

			worker.onerror = (e: ErrorEvent) => {
				const err = new Error(e.message || "Worker error");
				readyReject(err);
				onError(err);
			};

			// Send init message with the compiled WASM module.
			worker.postMessage({
				type: "uno:worker:init",
				wasmModule: wasmModule,
				unoConfig: unoConfig,
				args: args,
				envVars: envVars,
				dotnetJsUrl: dotnetJsUrl,
				appBaseUrl: appBaseUrl,
				frameworkBaseUrl: frameworkBaseUrl,
			});

			const handle: WorkerHandle = {
				postMessage: (data: any) => {
					worker.postMessage({
						type: "uno:worker:message",
						payload: data,
					});
				},
				terminate: () => {
					worker.terminate();
					URL.revokeObjectURL(blobUrl);
				},
				ready: readyPromise,
			};

			return handle;
		}

		// ---- Bridge methods for C# [JSImport] interop ----

		private static _activeHandle: WorkerHandle | null = null;
		private static _onMessageCallback: ((json: string) => void) | null = null;
		private static _onErrorCallback: ((error: string) => void) | null = null;

		/**
		 * Fork to a worker with [JSImport]-compatible signature.
		 * Messages and errors are routed to callbacks registered via
		 * setOnMessageCallback / setOnErrorCallback.
		 */
		public static fork(args: string[]): void {
			// Register utility for writing test results to a DOM element.
			if (typeof document !== "undefined") {
				(<any>globalThis).__unoSetResultDiv = (json: string) => {
					let el = document.getElementById("results");
					if (!el) {
						el = document.createElement("div");
						el.id = "results";
						document.body.appendChild(el);
					}
					el.textContent = json;
				};
			}

			const handle = WorkerFork.forkToWorker({
				args: args,
				onMessage: (data: any) => {
					if (WorkerFork._onMessageCallback) {
						WorkerFork._onMessageCallback(JSON.stringify(data));
					}
				},
				onError: (error: any) => {
					if (WorkerFork._onErrorCallback) {
						WorkerFork._onErrorCallback(error?.message || String(error));
					}
				},
			});

			WorkerFork._activeHandle = handle;

			handle.ready.then(() => {
				// Signal readiness by dispatching a special "ready" message.
				if (WorkerFork._onMessageCallback) {
					WorkerFork._onMessageCallback(JSON.stringify({ __workerReady: true }));
				}
			});
		}

		/**
		 * Send a JSON message to the active worker.
		 */
		public static sendMessage(json: string): void {
			if (!WorkerFork._activeHandle) {
				throw new Error("WorkerFork: No active worker. Call fork() first.");
			}
			WorkerFork._activeHandle.postMessage(JSON.parse(json));
		}

		/**
		 * Terminate the active worker.
		 */
		public static terminateWorker(): void {
			if (WorkerFork._activeHandle) {
				WorkerFork._activeHandle.terminate();
				WorkerFork._activeHandle = null;
			}
		}

		/**
		 * Register a callback for messages from the worker.
		 * Called from C# via [JSImport] with an Action<string>.
		 */
		public static setOnMessageCallback(callback: (json: string) => void): void {
			WorkerFork._onMessageCallback = callback;
		}

		/**
		 * Register a callback for worker errors.
		 * Called from C# via [JSImport] with an Action<string>.
		 */
		public static setOnErrorCallback(callback: (error: string) => void): void {
			WorkerFork._onErrorCallback = callback;
		}

		private static buildWorkerScript(): string {
			return `
"use strict";

let _pendingMessages = [];
let _messageCallback = null;

// Expose interop functions for C# [JSImport].
self.__unoWorkerPostMessage = function(jsonString) {
	self.postMessage({
		type: "uno:worker:message",
		payload: JSON.parse(jsonString),
	});
};

Object.defineProperty(self, "__unoWorkerMessageCallback", {
	get: function() { return _messageCallback; },
	set: function(fn) {
		_messageCallback = fn;
		// Deliver any messages that arrived before the callback was registered.
		while (_pendingMessages.length > 0) {
			fn(_pendingMessages.shift());
		}
	},
	configurable: true,
});

// Use addEventListener instead of self.onmessage to avoid triggering
// the .NET runtime's ENVIRONMENT_IS_WORKER detection, which checks
// globalThis.onmessage and skips promise resolution if it's set.
// See: https://github.com/dotnet/runtime/issues/114918
self.addEventListener("message", function _initHandler(e) {
	var msg = e.data;
	if (!msg || !msg.type) return;

	if (msg.type === "uno:worker:message") {
		if (_messageCallback) {
			_messageCallback(JSON.stringify(msg.payload));
		} else {
			_pendingMessages.push(JSON.stringify(msg.payload));
		}
		return;
	}

	if (msg.type !== "uno:worker:init") return;

	// Relay worker console to main thread for diagnostics.
	var _origConsole = { log: console.log, error: console.error, warn: console.warn, debug: console.debug };
	function _relay(level, args) {
		try {
			self.postMessage({ type: "uno:worker:log", level: level, text: Array.prototype.slice.call(args).map(String).join(" ") });
		} catch(_) {}
	}
	console.log = function() { _relay("log", arguments); _origConsole.log.apply(console, arguments); };
	console.error = function() { _relay("error", arguments); _origConsole.error.apply(console, arguments); };
	console.warn = function() { _relay("warn", arguments); _origConsole.warn.apply(console, arguments); };
	console.debug = function() { _relay("debug", arguments); _origConsole.debug.apply(console, arguments); };

	// Catch unhandled errors and rejections in the worker.
	self.addEventListener("error", function(ev) {
		console.error("[WorkerFork] Unhandled error:", ev.message, ev.filename, ev.lineno);
	});
	self.addEventListener("unhandledrejection", function(ev) {
		console.error("[WorkerFork] Unhandled rejection:", ev.reason);
	});

	(async function() {
	try {
		var wasmModule = msg.wasmModule;
		var unoConfig = msg.unoConfig;
		var args = msg.args || [];
		var envVars = msg.envVars || {};
		var dotnetJsUrl = msg.dotnetJsUrl;
		var appBaseUrl = msg.appBaseUrl;
		var frameworkBaseUrl = msg.frameworkBaseUrl;

		// Provide globalThis.Uno.WebAssembly.Bootstrap.Bootstrapper.invokeJS
		// so that C# [JSImport] calls to InvokeJS work in the worker.
		self.Uno = { WebAssembly: { Bootstrap: { Bootstrapper: {
			invokeJS: function(code) { return eval(code); }
		}}}};

		// Provide an explicit function to register the message callback.
		// C# code should prefer this over setting __unoWorkerMessageCallback directly,
		// as it correctly drains any messages that arrived before the callback was registered.
		self.__unoWorkerSetMessageCallback = function(fn) {
			_messageCallback = fn;
			while (_pendingMessages.length > 0) {
				fn(_pendingMessages.shift());
			}
		};

		console.log("[WorkerFork] Importing dotnet.js from: " + dotnetJsUrl);

		// Dynamically import the .NET runtime JS.
		var dotnetModule = await import(dotnetJsUrl);

		console.log("[WorkerFork] dotnet.js imported, initializing runtime...");

		// Initialize the .NET runtime.
		// If we received a pre-compiled WebAssembly.Module from the main thread,
		// provide an instantiateWasm callback to reuse it instead of re-fetching
		// and re-compiling dotnet.native.wasm. This shares the compiled code
		// in the browser's code cache, saving ~15-25 MB per worker.
		var builder = dotnetModule.dotnet
			.withEnvironmentVariables(envVars)
			.withDebugging(0);

		if (wasmModule) {
			// Intercept WebAssembly compilation APIs to reuse the pre-compiled module
			// from the main thread instead of re-compiling from scratch.
			// We do this at the global level rather than using withModuleConfig({instantiateWasm})
			// because withModuleConfig breaks the .NET runtime's JS eval path.
			console.log("[WorkerFork] Installing WebAssembly.Module reuse hooks");
			var _origInstantiateStreaming = WebAssembly.instantiateStreaming;
			var _origInstantiate = WebAssembly.instantiate;
			var _origCompileStreaming = WebAssembly.compileStreaming;
			var _origCompile = WebAssembly.compile;
			var _moduleUsed = false;

			WebAssembly.compileStreaming = function(source) {
				if (!_moduleUsed) {
					_moduleUsed = true;
					console.log("[WorkerFork] Reusing pre-compiled WebAssembly.Module (intercepted compileStreaming)");
					return Promise.resolve(wasmModule);
				}
				return _origCompileStreaming.apply(WebAssembly, arguments);
			};

			WebAssembly.compile = function(source) {
				if (!_moduleUsed) {
					_moduleUsed = true;
					console.log("[WorkerFork] Reusing pre-compiled WebAssembly.Module (intercepted compile)");
					return Promise.resolve(wasmModule);
				}
				return _origCompile.apply(WebAssembly, arguments);
			};

			WebAssembly.instantiateStreaming = function(source, imports) {
				if (!_moduleUsed) {
					_moduleUsed = true;
					console.log("[WorkerFork] Reusing pre-compiled WebAssembly.Module (intercepted instantiateStreaming)");
					return _origInstantiate.call(WebAssembly, wasmModule, imports).then(function(instance) {
						return { instance: instance, module: wasmModule };
					});
				}
				return _origInstantiateStreaming.apply(WebAssembly, arguments);
			};

			WebAssembly.instantiate = function(source, imports) {
				if (!_moduleUsed && !(source instanceof WebAssembly.Module)) {
					_moduleUsed = true;
					console.log("[WorkerFork] Reusing pre-compiled WebAssembly.Module (intercepted instantiate)");
					return _origInstantiate.call(WebAssembly, wasmModule, imports).then(function(instance) {
						return { instance: instance, module: wasmModule };
					});
				}
				return _origInstantiate.apply(WebAssembly, arguments);
			};

			// Also skip downloading dotnet.native.wasm since we already have the
			// compiled module. Return a minimal empty response for the wasm resource.
			builder = builder.withResourceLoader(function(type, name, defaultUri, integrity, behavior) {
				if (behavior === "dotnetwasm") {
					console.log("[WorkerFork] Skipping download of " + name + " (using pre-compiled module)");
					return new Response(new ArrayBuffer(0), {
						status: 200,
						headers: { "content-type": "application/wasm" }
					});
				}
				return null; // Use default loading for all other resources.
			});
		}

		var dotnetRuntime = await builder.create();

		console.log("[WorkerFork] .NET runtime ready!");
		self.postMessage({ type: "uno:worker:ready" });

		await dotnetRuntime.runMain(unoConfig.uno_main, args);

	} catch (err) {
		console.error("[WorkerFork] Error:", err);
		self.postMessage({
			type: "uno:worker:error",
			error: err && err.message ? err.message : String(err),
		});
	}
	})();
});
`;
		}
	}
}
