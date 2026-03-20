/// <reference path="UnoConfig.ts"/>

namespace Uno.WebAssembly.Bootstrap {

	export class WorkerBootstrapper {

		public static async bootstrap(): Promise<void> {
			try {
				const workerScript = (self as any).location.href;
				const basePath = workerScript.substring(0, workerScript.lastIndexOf('/') + 1);

				const config = await WorkerBootstrapper.loadConfig(basePath);

				WorkerBootstrapper.setupInvokeJSShim();

				const frameworkPath = basePath + '_framework/';

				const dotnetModule: any = await import(frameworkPath + config.dotnet_js_filename);

				// When the log profiler is enabled, wrap Emscripten's Module.out to
			// suppress "log-profiler not called (0x...)" printf spam from Mono's
			// prof_jit_done (log.c). See Bootstrapper.ts for full explanation.
			const workerLogProfilerEnabled = config.environmentVariables?.["UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS"];
			const workerModuleConfig: any = {};
			if (workerLogProfilerEnabled) {
				const defaultOut = console.log.bind(console);
				workerModuleConfig.out = (message: string) => {
					if (typeof message === "string" && (
						message.startsWith("log-profiler not called") ||
						message.startsWith("log-profiler | taking heapshot") ||
						message.startsWith("take-heapshot-method:"))) {
						return;
					}
					defaultOut(message);
				};
			}

			dotnetModule.dotnet
					.withModuleConfig(workerModuleConfig)
					.withRuntimeOptions(config.uno_runtime_options || [])
					.withConfig({ loadAllSatelliteResources: config.uno_load_all_satellite_resources });

				const dotnetRuntime = await dotnetModule.default(
					(_context: any) => {
						return {
							disableDotnet6Compatibility: false,
							configSrc: <string>undefined,
							baseUrl: config.uno_app_base,
							mainScriptPath: '_framework/' + config.dotnet_js_filename,
							onConfigLoaded: (monoConfig: any) => {
								WorkerBootstrapper.configLoaded(config, monoConfig);
							},
							onDotnetReady: () => { },
						};
					}
				);

				(<any>globalThis).Module = dotnetRuntime.Module;
				(<any>globalThis).config = config;

				if (config.enable_memory_profiler) {
					EmscriptenMemoryProfilerSupport.initialize(config);
				}

				// Load WasmScripts dependencies (uno_dependencies) before runMain.
				// The main-app bootstrapper uses require.js, but workers run as
				// ES modules so we use dynamic import() instead.
				if (config.uno_dependencies && config.uno_dependencies.length > 0) {
					const depBase = (self as any).location.href.substring(0, (self as any).location.href.lastIndexOf('/') + 1);
					for (const dep of config.uno_dependencies) {
						try {
							await import(depBase + dep + '.js');
						} catch (e) {
							console.warn(`[WorkerBootstrapper] Failed to load dependency ${dep}: ${e}`);
						}
					}
				}


				// Signal readiness and register profiler handlers BEFORE runMain.
				// runMain may be long-running (e.g., a worker service that waits
				// on messages), so we must not block these on Main completing.
				// IMPORTANT: self.onmessage is NOT set before dotnet.create() —
				// the .NET runtime checks globalThis.onmessage to detect pthread
				// deputy workers, and setting it prematurely causes hangs.
				// At this point dotnet.create() has completed, so it's safe.
				(self as any).postMessage({ type: 'uno-worker-ready' });
				WorkerBootstrapper.registerProfilerCommandHandler(config, dotnetRuntime);
				WorkerBootstrapper.registerConsoleHelpers(config, dotnetRuntime);

				await dotnetRuntime.runMain(config.uno_main, []);

			} catch (e) {
				console.error(`Worker .NET runtime initialization failed (${e})`);
				(self as any).postMessage({ type: 'uno-worker-error', error: `${e}` });
			}
		}

		private static configLoaded(config: UnoConfig, monoConfig: any): void {
			// Propagate environment variables from uno-config.js to the mono runtime
			if (config.environmentVariables) {
				for (let key in config.environmentVariables) {
					if (config.environmentVariables.hasOwnProperty(key)) {
						monoConfig.environmentVariables[key] = config.environmentVariables[key];
					}
				}
			}

			// Enable log profiler if configured
			var logProfilerConfig = config.environmentVariables
				? config.environmentVariables["UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS"]
				: null;

			if (logProfilerConfig) {
				monoConfig.logProfilerOptions = {
					configuration: logProfilerConfig,
					// takeHeapshot is required by the .NET runtime assert in profiler.ts.
					// In interpreter mode this registers a JIT-done callback that never
					// matches (methods aren't JIT'd), producing "log-profiler not called"
					// printf spam — suppressed via the Module.out filter installed in
					// bootstrap() before dotnet.create().
					takeHeapshot: "Uno.LogProfilerSupport:FlushProfile"
				};
			}

			// Enable AOT profiler if configured
			if (config.generate_aot_profile) {
				monoConfig.aotProfilerOptions = {
					writeAt: "Uno.AotProfilerSupport::StopProfile",
					sendTo: "System.Runtime.InteropServices.JavaScript.JavaScriptExports::DumpAotProfileData"
				};
			}
		}

		private static async loadConfig(basePath: string): Promise<UnoConfig> {
			const packagePath = (<any>self).__unoWorkerPackagePath || '';
			const configUrl = basePath + packagePath + 'uno-config.js';

			const configResponse = await fetch(configUrl);
			if (!configResponse.ok) {
				throw new Error(`Failed to load worker config from ${configUrl}: ${configResponse.status} ${configResponse.statusText}`);
			}
			let configText = await configResponse.text();

			// Strip ES module export syntax (incompatible with classic workers)
			// and replace block-scoped `let config` with function-scoped `var config`
			// so bare `config.xxx = ...` references work inside the Function body.
			configText = configText.replace(/export\s*\{[^}]*\};?\s*$/, '');
			configText = configText.replace(/\blet\s+config\b/, 'var config');
			const configFn = new Function(configText + '\nreturn config;');
			const config = configFn();
			(<any>self).config = config;
			return config as UnoConfig;
		}

		/**
		 * Exposes profiler helper functions on globalThis so developers can
		 * call them from the worker's DevTools console (Sources > worker.js).
		 *
		 * Each function posts the profiler data to the host via postMessage.
		 * The host should listen for `uno-profiler-data` messages and trigger
		 * a file download (see documentation for the host-side handler).
		 */
		private static registerConsoleHelpers(config: UnoConfig, dotnetRuntime: any): void {
			const g = globalThis as any;

			g.saveMemoryProfile = function (format?: string) {
				WorkerBootstrapper.handleMemorySnapshot(format || 'speedscope');
				console.info('[WorkerProfiler] Memory snapshot posted to host for download.');
			};

			g.saveLogProfile = function () {
				WorkerBootstrapper.handleLogProfilerSave(dotnetRuntime);
				console.info('[WorkerProfiler] Log profiler data posted to host for download.');
			};

			g.saveAotProfile = function () {
				WorkerBootstrapper.handleAotProfilerSave(dotnetRuntime);
				console.info('[WorkerProfiler] AOT profile posted to host for download.');
			};

			// Print available commands
			const commands: string[] = [];
			if (config.enable_memory_profiler) {
				commands.push('saveMemoryProfile("speedscope") or saveMemoryProfile("perfview")');
			}
			if (config.environmentVariables && config.environmentVariables["UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS"]) {
				commands.push('saveLogProfile()');
			}
			if (config.generate_aot_profile) {
				commands.push('saveAotProfile()');
			}

			if (commands.length > 0) {
				console.info(
					'[WorkerProfiler] Available profiler commands (run from this worker console):\n  ' +
					commands.join('\n  '));
			}
		}

		/**
		 * Registers a message handler so the host can request profiler data.
		 *
		 * Supported commands (sent via worker.postMessage):
		 *   { type: 'uno-profiler-command', command: 'memory-snapshot', format?: 'speedscope'|'perfview' }
		 *   { type: 'uno-profiler-command', command: 'log-profiler-save' }
		 *   { type: 'uno-profiler-command', command: 'aot-profiler-save' }
		 *
		 * The worker replies with:
		 *   { type: 'uno-profiler-data', command, filename, data (base64) }
		 *   { type: 'uno-profiler-error', command, error }
		 */
		private static registerProfilerCommandHandler(_config: UnoConfig, dotnetRuntime: any): void {
			self.addEventListener("message", (e: any) => {
				const msg = e.data;
				if (!msg || msg.type !== 'uno-profiler-command') {
					return;
				}

				try {
					switch (msg.command) {
						case 'memory-snapshot':
							WorkerBootstrapper.handleMemorySnapshot(msg.format || 'speedscope');
							break;

						case 'log-profiler-save':
							WorkerBootstrapper.handleLogProfilerSave(dotnetRuntime);
							break;

						case 'aot-profiler-save':
							WorkerBootstrapper.handleAotProfilerSave(dotnetRuntime);
							break;

						default:
							console.warn(`[WorkerProfiler] Unknown command: ${msg.command}`);
					}
				} catch (err) {
					(self as any).postMessage({
						type: 'uno-profiler-error',
						command: msg.command,
						error: `${err}`
					});
				}
			});
		}

		private static handleMemorySnapshot(format: string): void {
			const profiler = (<any>globalThis).emscriptenMemoryProfiler;
			if (!profiler) {
				(self as any).postMessage({
					type: 'uno-profiler-error',
					command: 'memory-snapshot',
					error: 'Memory profiler not available'
				});
				return;
			}

			// Reuse the existing snapshot builder from EmscriptenMemoryProfilerSupport
			const json = format === 'perfview'
				? EmscriptenMemoryProfilerSupport.buildPerfViewJson()
				: EmscriptenMemoryProfilerSupport.buildSpeedscopeJson();

			const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
			const ext = format === 'perfview' ? 'PerfView.json' : 'speedscope.json';

			(self as any).postMessage({
				type: 'uno-profiler-data',
				command: 'memory-snapshot',
				filename: `memory-profile-${timestamp}.${ext}`,
				data: btoa(json)
			});
		}

		private static async handleLogProfilerSave(dotnetRuntime: any): Promise<void> {
		const mod = dotnetRuntime.Module;
		if (!mod || !mod.FS) {
			(self as any).postMessage({
				type: 'uno-profiler-error',
				command: 'log-profiler-save',
				error: 'Module.FS not available'
			});
			return;
		}

		try {
			const exports = await dotnetRuntime.getAssemblyExports("Uno.Wasm.LogProfiler");
			const flush = exports.Uno.LogProfilerSupport.FlushProfile;
			const getPath = exports.Uno.LogProfilerSupport.GetProfilerProfileOutputFile;

			if (flush) {
				flush();
				const profilePath = getPath ? getPath() : "output.mlpd";
				const stat = mod.FS.stat(profilePath);

				if (stat && stat.size > 0) {
					const data: Uint8Array = mod.FS.readFile(profilePath);

					(self as any).postMessage({
						type: 'uno-profiler-data',
						command: 'log-profiler-save',
						filename: 'profile.mlpd',
						data: WorkerBootstrapper.uint8ArrayToBase64(data)
					});
					return;
				}
			}
		} catch (err) {
			console.error(`[WorkerProfiler] Log profiler error: ${err}`);
		}

		(self as any).postMessage({
			type: 'uno-profiler-error',
			command: 'log-profiler-save',
			error: 'Log profiler data not available'
		});
	}

		private static handleAotProfilerSave(dotnetRuntime: any): void {
			try {
				const getExports = dotnetRuntime.getAssemblyExports;
				if (!getExports) {
					throw 'getAssemblyExports not available';
				}

				const exports = getExports("Uno.Wasm.AotProfiler");
				exports.Uno.AotProfilerSupport.StopProfile();

				const profileData: Uint8Array = dotnetRuntime.INTERNAL?.aotProfileData;
				if (profileData && profileData.length > 0) {
					(self as any).postMessage({
						type: 'uno-profiler-data',
						command: 'aot-profiler-save',
						filename: 'aot.profile',
						data: WorkerBootstrapper.uint8ArrayToBase64(profileData)
					});
					return;
				}
			} catch (err) {
				console.error(`[WorkerProfiler] AOT profiler error: ${err}`);
			}

			(self as any).postMessage({
				type: 'uno-profiler-error',
				command: 'aot-profiler-save',
				error: 'AOT profiler data not available'
			});
		}

		/**
		 * Converts a Uint8Array to a base64 string using chunked processing
		 * to avoid quadratic string concatenation and stack overflow on large arrays.
		 */
		private static uint8ArrayToBase64(data: Uint8Array): string {
			const CHUNK_SIZE = 8192;
			const chunks: string[] = [];
			for (let i = 0; i < data.length; i += CHUNK_SIZE) {
				const slice = data.subarray(i, Math.min(i + CHUNK_SIZE, data.length));
				chunks.push(String.fromCharCode.apply(null, slice as any));
			}
			return btoa(chunks.join(''));
		}

		private static setupInvokeJSShim(): void {
			const g = globalThis as any;
			g.Uno = g.Uno || {};
			g.Uno.WebAssembly = g.Uno.WebAssembly || {};
			g.Uno.WebAssembly.Bootstrap = g.Uno.WebAssembly.Bootstrap || {};
			g.Uno.WebAssembly.Bootstrap.Bootstrapper = g.Uno.WebAssembly.Bootstrap.Bootstrapper || {};
			g.Uno.WebAssembly.Bootstrap.Bootstrapper.invokeJS = function (value: string) {
				return eval(value);
			};
		}
	}
}
