namespace Uno.WebAssembly.Bootstrap {

	export class AotProfilerSupport {

		private _context?: DotnetPublicAPI;
		private _aotProfilerExports?: any;

		private _unoConfig: UnoConfig;

		constructor(context: DotnetPublicAPI, unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig) {
			this._context = context;
			this._unoConfig = unoConfig;

			// This will fail when CSP is enabled, but initialization of the profiler
			// cannot happen asynchronously. Until this is fixed by the runtime, we'll need
			// to keep using bind_static_method.
			this.attachProfilerHotKey();
		}


		public static initialize(context: DotnetPublicAPI, unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig): AotProfilerSupport {
			if (Bootstrapper.ENVIRONMENT_IS_WEB && unoConfig.generate_aot_profile) {
				return new AotProfilerSupport(context, unoConfig);
			}
			return null;
		}

		private attachProfilerHotKey() {

			const altKeyName = navigator.platform.match(/^Mac/i) ? "Cmd" : "Alt";

			console.info(`AOT Profiler stop hotkey: Shift+${altKeyName}+P (when application has focus), or Run Uno.WebAssembly.Bootstrap.AotProfilerSupport.saveAotProfile() from the browser debug console.`);

			document.addEventListener(
				"keydown",
				(evt) => {
					if (evt.shiftKey && (evt.metaKey || evt.altKey) && evt.code === "KeyP") {
						this.saveAotProfile();
					}
				});
		}

		private async initializeProfile() {
			let anyContext = <any>this._context;

			if (anyContext.getAssemblyExports !== undefined) {
				this._aotProfilerExports = await anyContext.getAssemblyExports("Uno.Wasm.AotProfiler");
			}
			else {
				throw `Unable to find getAssemblyExports`;
			}
		}

		public async saveAotProfile() {
			await this.initializeProfile();

			this._aotProfilerExports.Uno.AotProfilerSupport.StopProfile();

			// Export the file
			var a = window.document.createElement('a');
			var blob = new Blob([(<any>this._context.INTERNAL).aotProfileData]);
			a.href = window.URL.createObjectURL(blob);
			a.download = "aot.profile";

			// Append anchor to body.
			document.body.appendChild(a);
			a.click();

			// Remove anchor from body
			document.body.removeChild(a);
		}

	}
}
