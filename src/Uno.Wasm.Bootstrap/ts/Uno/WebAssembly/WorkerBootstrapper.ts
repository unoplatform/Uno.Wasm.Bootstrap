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

				dotnetModule.dotnet
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

				await dotnetRuntime.runMain(config.uno_main, []);

				// Signal readiness AFTER runtime is fully initialized.
				// IMPORTANT: self.onmessage is NOT set before dotnet.create() —
				// the .NET runtime checks globalThis.onmessage to detect pthread
				// deputy workers, and setting it prematurely causes hangs.
				(self as any).postMessage({ type: 'uno-worker-ready' });

				// Register profiler command handler for host-initiated requests
				WorkerBootstrapper.registerProfilerCommandHandler(config, dotnetRuntime);

				// Expose convenience functions on globalThis so developers can
				// call them directly from the worker's DevTools console.
				WorkerBootstrapper.registerConsoleHelpers(config, dotnetRuntime);

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
					configuration: logProfilerConfig
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
			let configText = await configResponse.text();

			// Strip ES module export syntax (incompatible with classic workers)
			// and replace block-scoped `let config` with `self.config` so it
			// escapes the Function constructor's scope.
			configText = configText.replace(/export\s*\{[^}]*\};?\s*$/, '');
			configText = configText.replace(/\blet\s+config\b/, 'self.config');
			(new Function(configText))();

			return (<any>self).config as UnoConfig;
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

		private static handleLogProfilerSave(dotnetRuntime: any): void {
			const mod = dotnetRuntime.Module;
			if (!mod || !mod.FS) {
				(self as any).postMessage({
					type: 'uno-profiler-error',
					command: 'log-profiler-save',
					error: 'Module.FS not available'
				});
				return;
			}

			// Flush the log profiler and read the output file
			try {
				const binding = (<any>globalThis).BINDING ||
					(dotnetRuntime.BINDING);

				if (binding) {
					const flush = binding.bind_static_method(
						"[Uno.Wasm.LogProfiler] Uno.LogProfilerSupport:FlushProfile");
					const getPath = binding.bind_static_method(
						"[Uno.Wasm.LogProfiler] Uno.LogProfilerSupport:GetProfilerProfileOutputFile");

					flush();
					const profilePath = getPath();
					const stat = mod.FS.stat(profilePath);

					if (stat && stat.size > 0) {
						const data: Uint8Array = mod.FS.readFile(profilePath);
						// Convert to base64 for transfer
						let binary = '';
						for (let i = 0; i < data.length; i++) {
							binary += String.fromCharCode(data[i]);
						}

						(self as any).postMessage({
							type: 'uno-profiler-data',
							command: 'log-profiler-save',
							filename: 'profile.mlpd',
							data: btoa(binary)
						});
						return;
					}
				}
			} catch (err) {
				// Fall through to error
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
					let binary = '';
					for (let i = 0; i < profileData.length; i++) {
						binary += String.fromCharCode(profileData[i]);
					}

					(self as any).postMessage({
						type: 'uno-profiler-data',
						command: 'aot-profiler-save',
						filename: 'aot.profile',
						data: btoa(binary)
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
