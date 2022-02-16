namespace Uno.WebAssembly.Bootstrap {

	export class AotProfilerSupport {

		private _context?: DotnetPublicAPI;

        private _unoConfig: UnoConfig;

		constructor(context: DotnetPublicAPI, unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig) {
			this._context = context;
			this._unoConfig = unoConfig;

			var initializeProfile = this._context.BINDING.bind_static_method("[Uno.Wasm.AotProfiler] Uno.AotProfilerSupport:Initialize");
			if (initializeProfile) {
				initializeProfile();
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

		private attachProfilerHotKey() {

			const altKeyName = navigator.platform.match(/^Mac/i) ? "Cmd" : "Alt";

			console.info(`AOT Profiler stop hotkey: Shift+${altKeyName}+P (when application has focus), or Run this.saveAotProfile() from the browser debug console.`);

			document.addEventListener(
				"keydown",
				(evt) => {
					if (evt.shiftKey && (evt.metaKey || evt.altKey) && evt.code === "KeyP") {
						this.saveAotProfile();
					}
				});
		}

		private saveAotProfile() {
			var stopProfile = this._context.BINDING.bind_static_method("[Uno.Wasm.AotProfiler] Uno.AotProfilerSupport:StopProfile");
			stopProfile();

			// Export the file
			var a = window.document.createElement('a');
			var blob = new Blob([this._context.INTERNAL.aot_profile_data]);
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
