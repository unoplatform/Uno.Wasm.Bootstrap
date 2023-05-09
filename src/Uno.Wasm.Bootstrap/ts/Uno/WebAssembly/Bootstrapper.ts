/// <reference path="AotProfilerSupport.ts"/>
/// <reference path="HotReloadSupport.ts"/>
/// <reference path="LogProfilerSupport.ts"/>
/// <reference path="UnoConfig.ts"/>

namespace Uno.WebAssembly.Bootstrap {

	export class Bootstrapper {
		public disableDotnet6Compatibility: boolean;
		public configSrc: string;
		public onConfigLoaded: (config: MonoConfig) => void | Promise<void>;
		public onAbort: () => void;
		public onDotnetReady: () => void;
		public onDownloadResource: (request: ResourceRequest) => LoadingResource | undefined;

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

		static ENVIRONMENT_IS_WEB: boolean;
		static ENVIRONMENT_IS_WORKER: boolean;
		static ENVIRONMENT_IS_NODE: boolean;
		static ENVIRONMENT_IS_SHELL: boolean;

		constructor(unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig) {
			this._unoConfig = unoConfig;

			this._webAppBasePath = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_WEBAPP_BASE_PATH"];
			this._appBase = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_APP_BASE"];

			this.disableDotnet6Compatibility = false;
			this.configSrc = `mono-config.json`;
			this.onConfigLoaded = config => this.configLoaded(config);
			this.onDotnetReady = () => this.RuntimeReady();
			this.onAbort = () => this.runtimeAbort();
			this.onDownloadResource = (request) => <LoadingResource>{
				name: request.name,
				url: request.resolvedUrl,
				response: this.deobfuscateFile(request.resolvedUrl, this.fetchFile(request.resolvedUrl))
			};

			// Register this instance of the Uno namespace globally
			globalThis.Uno = Uno;
		}
		
		private async deobfuscateFile(asset: string, response: Promise<void | Response>): Promise<void | Response> {
			if (this._unoConfig.assemblyObfuscationKey && asset.endsWith(".dll")) {
				const responseValue = await response;

				if (responseValue) {
					var data = new Uint8Array(await responseValue.arrayBuffer());
					var key = this._unoConfig.assemblyObfuscationKey;

					for (var i = 0; i < data.length; i++) {
						data[i] ^= key.charCodeAt(i % key.length);
					}

					return new Response(data, { "status": 200, headers: responseValue.headers });
				}
			}
			
			return response;
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

				bootstrapper = new Bootstrapper(config.config);

				if (DOMContentLoaded) {
					bootstrapper.preInit();
				}

				//@ts-ignore
				var m = await import(`./dotnet.js`);

				const dotnetRuntime = await m.default(
					(context: DotnetPublicAPI) => {
						bootstrapper.configure(context);
						return bootstrapper.asDotnetConfig();
					}
				);

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
        }

		public asDotnetConfig(): DotnetModuleConfig {
			return <DotnetModuleConfig>{
				disableDotnet6Compatibility: this.disableDotnet6Compatibility,
				configSrc: this.configSrc,
				baseUrl: this._unoConfig.uno_app_base + "/",
				mainScriptPath: "dotnet.js",
				onConfigLoaded: this.onConfigLoaded,
				onDotnetReady: this.onDotnetReady,
				onAbort: this.onAbort,
				exports: ["IDBFS", "FS"].concat(this._unoConfig.emcc_exported_runtime_methods),
				downloadResource: this.onDownloadResource
			};
		}

		public configure(context: DotnetPublicAPI) {
			this._context = context;

			this._context.Module.ENVIRONMENT_IS_WEB = Bootstrapper.ENVIRONMENT_IS_WEB;
			this._context.Module.ENVIRONMENT_IS_NODE = Bootstrapper.ENVIRONMENT_IS_NODE;

			this.setupRequire();
			this.setupEmscriptenPreRun();
		}

		private async setupHotReload() {
			if (this._context.Module.ENVIRONMENT_IS_WEB && this.hasDebuggingEnabled()) {
				await HotReloadSupport.tryInitializeExports(this._getAssemblyExports);

				this._hotReloadSupport = new HotReloadSupport(this._context, this._unoConfig);
			}
		}

		private setupEmscriptenPreRun() {
			if (!this._context.Module.preRun) {
				this._context.Module.preRun = [];
			}
			else if (typeof this._context.Module.preRun === "function") {
				this._context.Module.preRun = [];
			}
			(<any>this._context.Module.preRun).push(() => this.wasmRuntimePreRun());
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

			if (this._unoConfig.environmentVariables) {
				for (let key in this._unoConfig.environmentVariables) {
					if (this._unoConfig.environmentVariables.hasOwnProperty(key)) {
						if (this._monoConfig.debugLevel) console.log(`Setting ${key}=${this._unoConfig.environmentVariables[key]}`);
						this._monoConfig.environmentVariables[key] = this._unoConfig.environmentVariables[key];
					}
				}
			}

			this.timezonePreSetup();

			if (LogProfilerSupport.initializeLogProfiler(this._unoConfig)) {
				this._logProfiler = new LogProfilerSupport(this._context, this._unoConfig);
			}
		}

		private timezonePreSetup() {
			let timeZone = 'UTC';
			try {
				timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
				// eslint-disable-next-line no-empty
			} catch { }
			this._monoConfig.environmentVariables['TZ'] = timeZone || 'UTC';
		}

		private RuntimeReady() {
			MonoRuntimeCompatibility.initialize();

			this.configureGlobal();

			this.initializeRequire();
			this._aotProfiler = AotProfilerSupport.initialize(this._context, this._unoConfig);
			this._logProfiler?.postInitializeLogProfiler();
		}

		private configureGlobal() {
			var thatGlobal = (<any>globalThis);

			thatGlobal.config = this._unoConfig;
			thatGlobal.MonoRuntime = MonoRuntimeCompatibility;

			// global exports from emscripten that are not exposed
			// as .NET is initialized in a module
			// List of possible exports: https://github.com/emscripten-core/emscripten/blob/c834ef7d69ccb4100239eeba0b0f6573fed063bc/src/modules.js#L391
			// Needs to be aligned with exports in https://github.com/unoplatform/Uno.DotnetRuntime.WebAssembly/blob/f7294fe410705bc220e63fc51d44bdffe4093a5d/patches/fix-additional-emscripten-exports.patch#L19
			// And in the packager's list of exports.
			thatGlobal.lengthBytesUTF8 = (<any>this._context.Module).lengthBytesUTF8;
			thatGlobal.stringToUTF8 = (<any>this._context.Module).stringToUTF8;
			thatGlobal.UTF8ToString = (<any>this._context.Module).UTF8ToString;
			thatGlobal.UTF8ArrayToString = (<any>this._context.Module).UTF8ArrayToString;
		}

		// This is called during emscripten `preInit` event, after we fetched config.
		private configLoaded(config: MonoConfig) {
			this._monoConfig = config;

			if (this._unoConfig.generate_aot_profile) {
				this._monoConfig.aotProfilerOptions = <AOTProfilerOptions>{
					writeAt: "Uno.AotProfilerSupport::StopProfile",
					sendTo: "Uno.AotProfilerSupport::DumpAotProfileData"
				};
			}

			var logProfilerConfig = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS"];
			if (logProfilerConfig) {
				this._monoConfig.logProfilerOptions = <LogProfilerOptions>{
					configuration: logProfilerConfig
				};
			}
		}

		private runtimeAbort() {
			// set_exit_code(1, error);
		}

		public preInit() {
			this.body = document.getElementById("uno-body");

			this.initProgress();
		}

		private async mainInit(): Promise<void> {
			try {
				this.attachDebuggerHotkey();
				await this.setupHotReload();
				await this.timezoneSetup();

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

		private async timezoneSetup() {
			let timezoneDataExports = await this._getAssemblyExports("Uno.Wasm.TimezoneData");

			timezoneDataExports.Uno.Wasm.TimezoneData.TimezoneHelper.Setup(
                Intl.DateTimeFormat().resolvedOptions().timeZone
            );
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

		private initProgress() {
			this.loader = this.body.querySelector(".uno-loader");

			if (this.loader) {
				this.loader.id = "loading";
				const totalBytesToDownload = this._unoConfig.mono_wasm_runtime_size + this._unoConfig.total_assemblies_size;
				const progress = this.loader.querySelector("progress");
				progress.max = totalBytesToDownload;
				(<any>progress).value = ""; // indeterminate
				this.progress = progress;

				if (this.loader.parentElement == this.body) {

					this.bodyObserver = new MutationObserver(() => {
						if (!this.loader.classList.contains("uno-keep-loader")){
							// This version of Uno Platform cannot remove
							// bootstrapper's loader, so we must do it.
							this.loader.parentNode.removeChild(this.loader);
						}
						this.bodyObserver.disconnect();
						this.bodyObserver = null;
					});

					this.bodyObserver.observe(this.body, { childList: true });
				}

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
				if (manifest && manifest.splashScreenImage) {
					if (!manifest.splashScreenImage.match(/^(http(s)?:\/\/.)/g)) {
						// Local images need to be prefixed with the app based path
						manifest.splashScreenImage = `${this._unoConfig.uno_app_base}/${manifest.splashScreenImage}`;
					}

					img.setAttribute("src", manifest.splashScreenImage);
				} else {
					img.setAttribute("src", "https://uno-assets.platform.uno/logos/uno-splashscreen-light.png");
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

		private reportProgressWasmLoading(loaded: number) {
			if (this.progress) {
				this.progress.value = loaded;
			}
		}

		private reportAssemblyLoading(adding: number) {
			if (this.progress) {
				this.progress.value += adding;
			}
		}

		private raiseLoadingError(err: any) {
			this.loader.setAttribute("loading-alert", "error");

			const alert = this.loader.querySelector(".alert");

			let title = alert.getAttribute("title");
			if (title) {
				title += `\n${err}`;
			} else {
				title = `${err}`;
			}
			alert.setAttribute("title", title);
		}

		private raiseLoadingWarning(msg: string) {
			if (this.loader.getAttribute("loading-alert") !== "error") {
				this.loader.setAttribute("loading-alert", "warning");
			}

			const alert = this.loader.querySelector(".alert");

			let title = alert.getAttribute("title");
			if (title) {
				title += `\n${msg}`;
			} else {
				title = `${msg}`;
			}
			alert.setAttribute("title", title);
		}

		private getFetchInit(url: string): RequestInit {
			const fileName = url.substring(url.lastIndexOf("/") + 1);

			const init: RequestInit = { credentials: "omit" };

			if (this._unoConfig.files_integrity.hasOwnProperty(fileName)) {
				init.integrity = this._unoConfig.files_integrity[fileName];
			}

			return init;
		}

		private fetchWithProgress(url: string, progressCallback: Function) : Promise<void | Response> {

			if (!this.loader) {
				// No active loader, simply use the fetch API directly...
				return fetch(url, this.getFetchInit(url));
			}

			return fetch(url, this.getFetchInit(url))
				.then(response => {
					if (!response.ok) {
						throw Error(`${response.status} ${response.statusText}`);
					}

					try {
						let loaded = 0;

						// Wrap original stream with another one, while reporting progress.
						const stream = new ReadableStream({
							start(ctl) {
								const reader = response.body.getReader();

								read();

								function read() {
									reader.read()
										.then(
											({ done, value }) => {
												if (done) {
													ctl.close();
													return;
												}
												loaded += value.byteLength;
												progressCallback(loaded, value.byteLength);
												ctl.enqueue(value);
												read();
											})
										.catch(error => {
											console.error(error);
											ctl.error(error);
										});
								}
							}
						});

						// We copy the previous response to keep original headers.
						// Not only the WebAssembly will require the right content-type,
						// but we also need it for streaming optimizations:
						// https://bugs.chromium.org/p/chromium/issues/detail?id=719172#c28
						return new Response(stream, response);
					}
					catch (ex) {
						// ReadableStream may not be supported (Edge as of 42.17134.1.0)
						return response;
					}
				})
				.catch(err => this.raiseLoadingError(err));
		}

		private fetchFile(asset: string) : Promise<void | Response> {

			if (asset.lastIndexOf(".dll") !== -1) {
				asset = asset.replace(".dll", this._unoConfig.assemblyFileExtension);

				if (this._unoConfig.assemblyFileNameObfuscationMode == "NoDots") {
					asset = asset.replace(/\/([^\/]*)$/, function (match, p1) {
						return "/" + p1.replace(/\./g, "_");
					});	
				}
			}

			if (asset.startsWith("icudt") && asset.endsWith(".dat")) {
				asset = `${this._unoConfig.uno_app_base}/${asset}`;
			}

			asset = asset.replace("/managed/", `/${this._unoConfig.uno_remote_managedpath}/`);

			if (this._context.Module.ENVIRONMENT_IS_NODE) {
				var fs = require('fs');

				console.log('Loading... ' + asset);
				var binary = fs.readFileSync(asset);
				var resolve_func2 = function (resolve: any, reject: any) {
					resolve(new Uint8Array(binary));
				};
				var resolve_func1 = function (resolve: any, reject: any) {
					var response = {
						ok: true,
						url: asset,
						arrayBuffer: function () {
							return new Promise(resolve_func2);
						}
					};
					resolve(response);
				};
				return new Promise(resolve_func1);
			} else {
				if (!this._unoConfig.enable_debugging) {
					// Assembly fetch streaming is disabled during debug, it seems to
					// interfere with the ability for mono or the chrome debugger to 
					// initialize the debugging session properly. Streaming in debug is
					// not particularly interesting, so we can skip it.

					const assemblyName = asset.substring(asset.lastIndexOf("/") + 1);
					if (this._unoConfig.assemblies_with_size.hasOwnProperty(assemblyName)) {
						return this
							.fetchWithProgress(asset, (loaded: any, adding: any) => this.reportAssemblyLoading(adding));
					}
				}

				return fetch(asset);
			}
		}

		private isElectron() {
			return navigator.userAgent.indexOf('Electron') !== -1;
		}

		private initializeRequire() {

			// Uno.Wasm.Bootstrap is using "requirejs" by default, which is an AMD implementation
			// But when run with NodeJS or Electron, it's using CommonJS instead of AMD
			this._isUsingCommonJS = this._unoConfig.uno_shell_mode !== "BrowserEmbedded" && (this._context.Module.ENVIRONMENT_IS_NODE || this.isElectron());

			if (this._unoConfig.enable_debugging) console.log("Done loading the BCL");

			if (this._unoConfig.uno_dependencies && this._unoConfig.uno_dependencies.length !== 0) {
				let pending = this._unoConfig.uno_dependencies.length;

				const checkDone = (dependency: string) => {
					--pending;
					if (this._unoConfig.enable_debugging) console.log(`Loaded dependency (${dependency}) - remains ${pending} other(s).`);
					if (pending === 0) {
						this.mainInit();
					}
				};

				this._unoConfig.uno_dependencies.forEach((dependency) => {
					if (this._unoConfig.enable_debugging) console.log(`Loading dependency (${dependency})`);

					let processDependency = (instance: any) => {

						// If the module is built on emscripten, intercept its loading.
						if (instance && instance.HEAP8 !== undefined) {

							const existingInitializer = instance.onRuntimeInitialized;

							if (this._unoConfig.enable_debugging) console.log(`Waiting for dependency (${dependency}) initialization`);

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
			return this._hasReferencedPdbs && this._currentBrowserIsChrome;
		}

		private attachDebuggerHotkey() {
			if (this._context.Module.ENVIRONMENT_IS_WEB) {

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
