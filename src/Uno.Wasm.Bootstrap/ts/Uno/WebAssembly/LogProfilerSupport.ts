namespace Uno.WebAssembly.Bootstrap {

	export class LogProfilerSupport {

		private _context?: DotnetPublicAPI;

		private static _logProfilerEnabled: boolean;
		private _flushLogProfile: Function;
		private _getLogProfilerProfileOutputFile: Function;
		private triggerHeapShotLogProfiler: Function;
        private _unoConfig: UnoConfig;

		constructor(context: DotnetPublicAPI, unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig) {
			this._context = context;
			this._unoConfig = unoConfig;
		}

		public static initializeLogProfiler(unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig): boolean {
			const options = unoConfig.environmentVariables["UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS"];
			if (options) {
				Module.ccall('mono_wasm_load_profiler_log', null, ['string'], [options]);

				this._logProfilerEnabled = true;

				return true;
			}

			return false;
		}

		public postInitializeLogProfiler() {
			if (LogProfilerSupport._logProfilerEnabled) {

				this.attachHotKey();

				setInterval(() => {
					this.ensureInitializeProfilerMethods();
					this._flushLogProfile();
				}, 5000);
			}
		}

		private attachHotKey() {

			if (this._context.Module.ENVIRONMENT_IS_WEB) {

				if (LogProfilerSupport._logProfilerEnabled) {
					// Use the combination shift+alt+D because it isn't used by the major browsers
					// for anything else by default
					const altKeyName = navigator.platform.match(/^Mac/i) ? "Cmd" : "Alt";

					console.info(`Log Profiler save hotkey: Shift+${altKeyName}+P (when application has focus), or Run this.saveLogProfile() from the browser debug console.`);
					document.addEventListener(
						"keydown",
						(evt) => {
							if (evt.shiftKey && (evt.metaKey || evt.altKey) && evt.code === "KeyP") {
								this.saveLogProfile();
							}
						});

					console.info(`Log Profiler take heap shot hotkey: Shift+${altKeyName}+H (when application has focus), or Run this.takeHeapShot() from the browser debug console.`);
					document.addEventListener(
						"keydown",
						(evt) => {
							if (evt.shiftKey && (evt.metaKey || evt.altKey) && evt.code === "KeyH") {
								this.takeHeapShot();
							}
						});
				}

			}
		}

		private ensureInitializeProfilerMethods() {
			if (LogProfilerSupport._logProfilerEnabled && !this._flushLogProfile) {
				this._flushLogProfile = this._context.BINDING.bind_static_method("[Uno.Wasm.LogProfiler] Uno.LogProfilerSupport:FlushProfile");
				this._getLogProfilerProfileOutputFile = this._context.BINDING.bind_static_method("[Uno.Wasm.LogProfiler] Uno.LogProfilerSupport:GetProfilerProfileOutputFile");
				this.triggerHeapShotLogProfiler = this._context.BINDING.bind_static_method("[Uno.Wasm.LogProfiler] Uno.LogProfilerSupport:TriggerHeapShot");
			}
		}

		private takeHeapShot() {
			this.ensureInitializeProfilerMethods();

			this.triggerHeapShotLogProfiler();
		}

		private readProfileFile() {
			this.ensureInitializeProfilerMethods();

			this._flushLogProfile();
			var profileFilePath = this._getLogProfilerProfileOutputFile();

			var stat = FS.stat(profileFilePath);

			if (stat && stat.size > 0) {
				return FS.readFile(profileFilePath);
			}
			else {
				return null;
			}
		}

		private saveLogProfile() {
			this.ensureInitializeProfilerMethods();

			var profileArray = this.readProfileFile();

			var a = window.document.createElement('a');
			a.href = window.URL.createObjectURL(new Blob([profileArray]));
			a.download = "profile.mlpd";

			// Append anchor to body.
			document.body.appendChild(a);
			a.click();

			// Remove anchor from body
			document.body.removeChild(a);
		}
	}
}
