/// <reference path="AotProfilerSupport.ts"/>
/// <reference path="HotReloadSupport.ts"/>
/// <reference path="UnoConfig.ts"/>

namespace Uno.WebAssembly.Bootstrap {

	export class Bootstrapper {
		public disableDotnet6Compatibility: boolean;
		public configSrc: string;
		public onConfigLoaded: (config: MonoConfig) => void | Promise<void>;
		public onAbort: () => void;
		public onDotnetReady: () => void;

		private _context?: DotnetPublicAPI;
		private _monoConfig: MonoConfig;
		private _unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig;

		private _getAssemblyExports: any;

		private _hotReloadSupport?: HotReloadSupport;
		private _logProfiler?: LogProfilerSupport;
		private _aotProfiler?: AotProfilerSupport;

		private _runMain: (mainAssemblyName: string, args: string[]) => Promise<number>;

		private _webAppBasePath: string;
		private _appBase: string;

		private body: HTMLElement;
		private bodyObserver: MutationObserver;
		private loader: HTMLElement;
		private progress: HTMLProgressElement;

		private _isUsingCommonJS: boolean;
		private _currentBrowserIsChrome: boolean;
		private _hasReferencedPdbs: boolean;
		private _previousTotalResources: number;
		private _currentTargetProgress: number;

		static ENVIRONMENT_IS_WEB: boolean;
		static ENVIRONMENT_IS_WORKER: boolean;
		static ENVIRONMENT_IS_NODE: boolean;
		static ENVIRONMENT_IS_SHELL: boolean;

		constructor(unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig) {
			this._unoConfig = unoConfig;

			this._webAppBasePath = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_WEBAPP_BASE_PATH"];
			this._appBase = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_APP_BASE"];

			this.disableDotnet6Compatibility = false;
			this.onConfigLoaded = config => this.configLoaded(config);
			this.onDotnetReady = () => this.RuntimeReady();

			// Register this instance of the Uno namespace globally
			globalThis.Uno = Uno;
		}

		public static invokeJS(value: string) {
			return eval(value);
		}

		public static async bootstrap(): Promise<void> {

			try {

				// Extract of https://github.com/emscripten-core/emscripten/blob/2.0.23/src/shell.js#L99-L104
				Bootstrapper.ENVIRONMENT_IS_WEB = typeof window === 'object';
				Bootstrapper.ENVIRONMENT_IS_WORKER = typeof (<any>globalThis).importScripts === 'function';
				// N.b. Electron.js environment is simultaneously a NODE-environment, but
				// also a web environment.
				Bootstrapper.ENVIRONMENT_IS_NODE = typeof (<any>globalThis).process === 'object' && typeof (<any>globalThis).process.versions === 'object' && typeof (<any>globalThis).process.versions.node === 'string';
				Bootstrapper.ENVIRONMENT_IS_SHELL = !Bootstrapper.ENVIRONMENT_IS_WEB && !Bootstrapper.ENVIRONMENT_IS_NODE && !Bootstrapper.ENVIRONMENT_IS_WORKER;

				let bootstrapper: Bootstrapper = null;
				let DOMContentLoaded = false;

				if (typeof window === 'object' /* ENVIRONMENT_IS_WEB */) {
					globalThis.document.addEventListener("DOMContentLoaded", () => {
						DOMContentLoaded = true;
						bootstrapper?.preInit();
					});
				}

				//@ts-ignore
				var config = await import('./uno-config.js');

				if (document && (document as any).uno_app_base_override) {
					config.config.uno_app_base = (document as any).uno_app_base_override;
				}

				bootstrapper = new Bootstrapper(config.config);

				if (DOMContentLoaded) {
					bootstrapper.preInit();
				}

				//@ts-ignore
				var m = await import(`../_framework/dotnet.js`);

				m.dotnet
					.withModuleConfig({
						preRun: () => bootstrapper.wasmRuntimePreRun(),
					})
					.withRuntimeOptions(config.config.uno_runtime_options)
					.withConfig({ loadAllSatelliteResources: config.config.uno_load_all_satellite_resources });

				const dotnetRuntime = await m.default(
					(context: DotnetPublicAPI) => {
						bootstrapper.configure(context);
						return bootstrapper.asDotnetConfig();
					}
				);

				// Capture the module instance and publish to globalThis.
				bootstrapper._context.Module = dotnetRuntime.Module;
				(<any>globalThis).Module = bootstrapper._context.Module;

				bootstrapper._runMain = dotnetRuntime.runMain;
				bootstrapper.setupExports(dotnetRuntime);
			}
			catch (e) {
				throw `.NET runtime initialization failed (${e})`
			}
		}

		private setupExports(dotnetRuntime: any) {
			this._getAssemblyExports = dotnetRuntime.getAssemblyExports;
			(<any>this._context.Module).getAssemblyExports = dotnetRuntime.getAssemblyExports;
			(<any>globalThis.Module).getAssemblyExports = dotnetRuntime.getAssemblyExports;
		}

		public asDotnetConfig(): DotnetModuleConfig {
			return <DotnetModuleConfig>{
				disableDotnet6Compatibility: this.disableDotnet6Compatibility,
				configSrc: this.configSrc,
				baseUrl: this._unoConfig.uno_app_base,
				mainScriptPath: "_framework/dotnet.js",
				onConfigLoaded: this.onConfigLoaded,
				onDotnetReady: this.onDotnetReady,
				onAbort: this.onAbort,
				exports: ["IDBFS", "FS"].concat(this._unoConfig.emcc_exported_runtime_methods),
				onDownloadResourceProgress: (resourcesLoaded: number, totalResources: number) => this.reportDownloadResourceProgress(resourcesLoaded, totalResources),
			};
		}

		public configure(context: DotnetPublicAPI) {
			this._context = context;

			// Required for hot reload (browser-link provided javascript file)
			(<any>globalThis).BINDING = this._context.BINDING;
		}

		private async setupHotReload() {
			if (Bootstrapper.ENVIRONMENT_IS_WEB && this.hasDebuggingEnabled()) {
				await HotReloadSupport.tryInitializeExports(this._getAssemblyExports);

				this._hotReloadSupport = new HotReloadSupport(this._context, this._unoConfig);
			}
		}

		/**
		 * Setup the global require.js library
		 *
		 * This setup is needed as .NET 7 sets up its own require function
		 * if none is present, and the bootstrapper uses a global require.js.
		 * */
		private setupRequire() {
			const anyModule = <any>this._context.Module;
			anyModule.imports = anyModule.imports || {};
			anyModule.imports.require = (<any>globalThis).require;
		}

		private wasmRuntimePreRun() {
			if (LogProfilerSupport.initializeLogProfiler(this._unoConfig)) {
				this._logProfiler = new LogProfilerSupport(this._context, this._unoConfig);
			}
		}

		private RuntimeReady() {
			this.configureGlobal();
			this.setupRequire();

			this.initializeRequire();
			this._aotProfiler = AotProfilerSupport.initialize(this._context, this._unoConfig);
		}

		private configureGlobal() {
			var thatGlobal = (<any>globalThis);

			thatGlobal.config = this._unoConfig;

			// The module instance is modified by the runtime, merge the changes
			thatGlobal.Module = this._context.Module;

			// global exports from emscripten that are not exposed
			// as .NET is initialized in a module
			// List of possible exports: https://github.com/emscripten-core/emscripten/blob/c834ef7d69ccb4100239eeba0b0f6573fed063bc/src/modules.js#L391
			// Needs to be aligned with exports in https://github.com/unoplatform/Uno.DotnetRuntime.WebAssembly/blob/f7294fe410705bc220e63fc51d44bdffe4093a5d/patches/fix-additional-emscripten-exports.patch#L19
			// And in the packager's list of exports.
			let anyModule = <any>this._context.Module;
			thatGlobal.lengthBytesUTF8 = anyModule.lengthBytesUTF8;
			thatGlobal.UTF8ToString = anyModule.UTF8ToString;
			thatGlobal.UTF8ArrayToString = anyModule.UTF8ArrayToString;

			thatGlobal.IDBFS = anyModule.IDBFS;
			thatGlobal.FS = anyModule.FS;

			// copy properties from this._unoConfig.emcc_exported_runtime_methods into globalThis
			if (this._unoConfig.emcc_exported_runtime_methods) {
				this._unoConfig.emcc_exported_runtime_methods.forEach((name: string) => {
					thatGlobal[name] = anyModule[name];
				});
			}
		}

		// This is called during emscripten `preInit` event, after we fetched config.
		private configLoaded(config: MonoConfig) {
			this._monoConfig = config;

			if (this._unoConfig.environmentVariables) {
				for (let key in this._unoConfig.environmentVariables) {
					if (this._unoConfig.environmentVariables.hasOwnProperty(key)) {
						if (this._monoConfig.debugLevel) console.log(`Setting ${key}=${this._unoConfig.environmentVariables[key]}`);
						this._monoConfig.environmentVariables[key] = this._unoConfig.environmentVariables[key];
					}
				}
			}

			if (this._unoConfig.generate_aot_profile) {
				this._monoConfig.aotProfilerOptions = <AOTProfilerOptions>{
					writeAt: "Uno.AotProfilerSupport::StopProfile",
					sendTo: "System.Runtime.InteropServices.JavaScript.JavaScriptExports::DumpAotProfileData"
				};
			}

			var logProfilerConfig = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS"];
			if (logProfilerConfig) {
				this._monoConfig.logProfilerOptions = <LogProfilerOptions>{
					configuration: logProfilerConfig
				};
			}

			// Initialize progress tracking with best-guess estimation
			this.initializeProgressEstimation();
		}

		public preInit() {
			this.body = document.getElementById("uno-body");

			this.initProgress();
		}

		private async mainInit(): Promise<void> {
			try {
				this.attachDebuggerHotkey();

				await this.setupHotReload();

				if (this._hotReloadSupport) {
					await this._hotReloadSupport.initializeHotReload();
				}

				this._runMain(this._unoConfig.uno_main, []);

				this.initializePWA();

			} catch (e) {
				console.error(e);
			}

			this.cleanupInit();
		}

		private cleanupInit() {
			if (this.progress) {
				this.progress.value = this.progress.max;
			}
			// Remove loader node if observer will not handle it
			if (!this.bodyObserver && this.loader && this.loader.parentNode) {
				this.loader.parentNode.removeChild(this.loader);
			}
		}

		private initializeProgressEstimation() {
			// Estimate the total number of resources based on the MonoConfig assets
			// This provides a better initial guess for the progress bar
			if (this._monoConfig && this._monoConfig.assets) {
				const estimatedTotal = this._monoConfig.assets.length;
				
				// Start with a more aggressive initial target if we have a good estimate
				// Use 30% of the estimated total as initial target to account for
				// the fact that .NET reports progress incrementally
				this._currentTargetProgress = Math.min(30, estimatedTotal * 0.3);
				this._previousTotalResources = 0;
				
				if (this._monoConfig.debugLevel) {
					console.log(`Progress estimation: ${estimatedTotal} assets in config, initial target: ${this._currentTargetProgress}`);
				}
			} else {
				// Fallback to conservative estimate if no config available
				this._currentTargetProgress = 30;
				this._previousTotalResources = 0;
			}
		}

		private reportDownloadResourceProgress(resourcesLoaded: number, totalResources: number) {
			this.progress.max = 100;
			
			// The totalResources value reported by .NET does not represent
			// the actual total. To prevent progress bar from jumping
			// setting the max progress to 100 instead with an initial target based on config estimation.
			// When total number of resources increases, we always increase the target by half of the remainder to 100
			// Usually the total grows several times, so ultimately the progress will appear as converging to completion.
			if (this._previousTotalResources != totalResources)
			{
				this._currentTargetProgress = this._currentTargetProgress + (100 - this._currentTargetProgress) / 2;
				this._previousTotalResources = totalResources;
			}
			
			this.progress.value = Math.min(resourcesLoaded, this._currentTargetProgress);
		}

		private initProgress() {
			this.loader = this.body.querySelector(".uno-loader");

			if (this.loader) {
				this.loader.id = "loading";
				const progress = this.loader.querySelector("progress");
				(<any>progress).value = ""; // indeterminate
				this.progress = progress;

				this.bodyObserver = new MutationObserver(() => {
					if (!this.loader.classList.contains("uno-keep-loader")) {
						// This version of Uno Platform cannot remove
						// bootstrapper's loader, so we must do it.
						this.loader.remove();
					}

					if (this.bodyObserver) {
						this.bodyObserver.disconnect();
						this.bodyObserver = null;
					}
				});

				this.bodyObserver.observe(this.body, { childList: true });

				// Used by Uno Platform to detect this bootstrapper version
				// can keep the loader displayed when requested
				this.loader.classList.add("uno-persistent-loader");
			}

			const configLoader = () => {
				if (manifest && manifest.lightThemeBackgroundColor) {
					this.loader.style.setProperty("--light-theme-bg-color", manifest.lightThemeBackgroundColor);
				}
				if (manifest && manifest.darkThemeBackgroundColor) {
					this.loader.style.setProperty("--dark-theme-bg-color", manifest.darkThemeBackgroundColor);
				}
				if (manifest && manifest.splashScreenColor && manifest.splashScreenColor != "transparent") {
					this.loader.style.setProperty("background-color", manifest.splashScreenColor);
				}
				if (manifest && manifest.accentColor) {
					this.loader.style.setProperty("--accent-color", manifest.accentColor);
				}
				if (manifest && manifest.lightThemeAccentColor) {
					this.loader.style.setProperty("--accent-color", manifest.lightThemeAccentColor);
				}
				if (manifest && manifest.darkThemeAccentColor) {
					this.loader.style.setProperty("--dark-theme-accent-color", manifest.darkThemeAccentColor);
				}
				const img = this.loader.querySelector("img");
				if (img) {
					if (manifest && manifest.splashScreenImage) {
						if (!manifest.splashScreenImage.match(/^(http(s)?:\/\/.)/g)) {
							// Local images need to be prefixed with the app based path
							manifest.splashScreenImage = `${this._unoConfig.uno_app_base}/${manifest.splashScreenImage}`;
						}

						img.setAttribute("src", manifest.splashScreenImage);
					} else {
						img.setAttribute("src", "https://uno-assets.platform.uno/logos/uno-splashscreen-light.png");
					}
				}
			};

			let manifest = (<any>window)["UnoAppManifest"];
			if (manifest) {
				configLoader();
			} else {

				for (var i = 0; i < this._unoConfig.uno_dependencies.length; i++) {
					if (this._unoConfig.uno_dependencies[i].endsWith('AppManifest')
						|| this._unoConfig.uno_dependencies[i].endsWith('AppManifest.js')) {
						require([this._unoConfig.uno_dependencies[i]], function () {
							manifest = (<any>window)["UnoAppManifest"];
							configLoader();
						});
						break;
					}
				}
			}
		}

		private isElectron() {
			return navigator.userAgent.indexOf('Electron') !== -1;
		}

		private initializeRequire() {

			// Uno.Wasm.Bootstrap is using "requirejs" by default, which is an AMD implementation
			// But when run with NodeJS or Electron, it's using CommonJS instead of AMD
			this._isUsingCommonJS = this._unoConfig.uno_shell_mode !== "BrowserEmbedded" && (Bootstrapper.ENVIRONMENT_IS_NODE || this.isElectron());

			if (this._unoConfig.uno_enable_tracing) console.log("Done loading the BCL");

			if (this._unoConfig.uno_dependencies && this._unoConfig.uno_dependencies.length !== 0) {
				let pending = this._unoConfig.uno_dependencies.length;

				const checkDone = (dependency: string) => {
					--pending;
					if (this._unoConfig.uno_enable_tracing) console.log(`Loaded dependency (${dependency}) - remains ${pending} other(s).`);
					if (pending === 0) {
						this.mainInit();
					}
				};

				this._unoConfig.uno_dependencies.forEach((dependency) => {
					if (this._unoConfig.uno_enable_tracing) console.log(`Loading dependency (${dependency})`);

					let processDependency = (instance: any) => {

						// If the module is built on emscripten, intercept its loading.
						if (instance && instance.HEAP8 !== undefined) {

							const existingInitializer = instance.onRuntimeInitialized;

							if (this._unoConfig.uno_enable_tracing) console.log(`Waiting for dependency (${dependency}) initialization`);

							instance.onRuntimeInitialized = () => {
								checkDone(dependency);

								if (existingInitializer)
									existingInitializer();
							};
						}
						else {
							checkDone(dependency);
						}
					};

					this.require([dependency], processDependency);
				});
			}
			else {
				// Reschedule the intialization to that the runtime can
				// properly initialize and exit `m.default` to provide dotnetRuntime.runMain
				setTimeout(() => {
					this.mainInit();
				}, 0);
			}
		}

		private require(modules: string[], callback: Function) {
			if (this._isUsingCommonJS) {
				modules.forEach(id => {
					// Emulate asynchronous process of AMD
					setTimeout(() => {
						const d = require('./' + id);
						callback(d);
					}, 0);
				});
			} else {
				if (typeof require === undefined) {
					throw `Require.js has not been loaded yet. If you have customized your index.html file, make sure that <script src="./require.js"></script> does not contain the defer directive.`;
				}

				require(modules, callback);
			}
		}

		private hasDebuggingEnabled() {
			return this._unoConfig.uno_debugging_enabled && this._currentBrowserIsChrome;
		}

		private attachDebuggerHotkey() {
			if (Bootstrapper.ENVIRONMENT_IS_WEB) {

				let loadAssemblyUrls = this._monoConfig.assets.map(a => a.name);

				//
				// Imported from https://github.com/aspnet/Blazor/tree/release/0.7.0
				//
				// History:
				//  2019-01-14: Adjustments to make the debugger helper compatible with Uno.Bootstrap.
				//

				this._currentBrowserIsChrome = (<any>window).chrome
					&& navigator.userAgent.indexOf("Edge") < 0; // Edge pretends to be Chrome

				this._hasReferencedPdbs = loadAssemblyUrls
					.some(function (url) { return /\.pdb$/.test(url); });

				// Use the combination shift+alt+D because it isn't used by the major browsers
				// for anything else by default
				const altKeyName = navigator.platform.match(/^Mac/i) ? "Cmd" : "Alt";

				if (this.hasDebuggingEnabled()) {
					console.info(`Debugging hotkey: Shift+${altKeyName}+D (when application has focus)`);
				}

				// Even if debugging isn't enabled, we register the hotkey so we can report why it's not enabled
				document.addEventListener("keydown", (evt) => {
					if (evt.shiftKey && (evt.metaKey || evt.altKey) && evt.code === "KeyD") {
						if (!this._hasReferencedPdbs) {
							console.error("Cannot start debugging, because the application was not compiled with debugging enabled.");
						}
						else if (!this._currentBrowserIsChrome) {
							console.error("Currently, only Chrome is supported for debugging.");
						}
						else {
							this.launchDebugger();
						}
					}
				});
			}
		}

		private launchDebugger() {

			//
			// Imported from https://github.com/aspnet/Blazor/tree/release/0.7.0
			//
			// History:
			//  2019-01-14: Adjustments to make the debugger helper compatible with Uno.Bootstrap.
			//

			// The noopener flag is essential, because otherwise Chrome tracks the association with the
			// parent tab, and then when the parent tab pauses in the debugger, the child tab does so
			// too (even if it's since navigated to a different page). This means that the debugger
			// itself freezes, and not just the page being debugged.
			//
			// We have to construct a link element and simulate a click on it, because the more obvious
			// window.open(..., 'noopener') always opens a new window instead of a new tab.
			const link = document.createElement("a");
			link.href = `_framework/debug?url=${encodeURIComponent(location.href)}`;
			link.target = "_blank";
			link.rel = "noopener noreferrer";
			link.click();
		}

		private initializePWA() {

			if (typeof window === 'object' /* ENVIRONMENT_IS_WEB */) {

				if (this._unoConfig.enable_pwa && 'serviceWorker' in navigator) {
					if (navigator.serviceWorker.controller) {
						console.debug("Active service worker found, skipping register");
					} else {
						const _webAppBasePath = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_WEBAPP_BASE_PATH"];

						console.debug(`Registering service worker for ${_webAppBasePath}`);

						navigator.serviceWorker
							.register(
								`${_webAppBasePath}service-worker.js`, {
								scope: _webAppBasePath,
								type: 'module'
							})
							.then(function () {
								console.debug('Service Worker Registered');
							});
					}
				}
			}
		}
	}
}
