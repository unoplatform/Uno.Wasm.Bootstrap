namespace Uno.WebAssembly.Bootstrap {

	export class AotProfilerSupport {

		private static _initializeProfile: Function;

		private _context?: DotnetPublicAPI;

        private _unoConfig: UnoConfig;

		constructor(context: DotnetPublicAPI, unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig) {
			this._context = context;
			this._unoConfig = unoConfig;

			if (AotProfilerSupport._initializeProfile) {
				AotProfilerSupport._initializeProfile();
				this.attachProfilerHotKey();
			}
			else {
				throw `Unable to find AOT Profiler initialization method`;
			}
		}

		public static initialize(context: DotnetPublicAPI, unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig): AotProfilerSupport {
			if (context.Module.ENVIRONMENT_IS_WEB && unoConfig.generate_aot_profile) {
				return new AotProfilerSupport(context, unoConfig);
			}
			return null;
		}

		public static async tryInitializeExports(unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig, getAssemblyExports: any) {
			if (unoConfig.generate_aot_profile) {
				let profilerExports = await getAssemblyExports("Uno.Wasm.LogProfiler");
				AotProfilerSupport._initializeProfile = profilerExports.Uno.AotProfilerSupport.Initialize;
			}
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

		public saveAotProfile() {
			var stopProfile = this._context.BINDING.bind_static_method("[Uno.Wasm.AotProfiler] Uno.AotProfilerSupport:StopProfile");
			stopProfile();

			// Export the file
			var a = window.document.createElement('a');
			var blob = new Blob([(<any>this._context.Module).aot_profile_data]);
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
