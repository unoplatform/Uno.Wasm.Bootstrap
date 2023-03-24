namespace Uno.WebAssembly.Bootstrap {

	export class LogProfilerSupport {

		private _context?: DotnetPublicAPI;

		private static _logProfilerEnabled: boolean;
		private _flushLogProfile: Function;
		private _getLogProfilerProfileOutputFile: Function;
		private _triggerHeapShotLogProfiler: Function;
        private _unoConfig: UnoConfig;
        private _getAssemblyExports: any;

		constructor(context: DotnetPublicAPI, unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig) {
			this._context = context;
			this._unoConfig = unoConfig;
		}

		public static initializeLogProfiler(unoConfig: Uno.WebAssembly.Bootstrap.UnoConfig): boolean {
			const options = unoConfig.environmentVariables["UNO_BOOTSTRAP_LOG_PROFILER_OPTIONS"];
			if (options) {
				this._logProfilerEnabled = true;
				return true;
			}
				
			return false;
		}

		public postInitializeLogProfiler(getAssemblyExports: any) {
			this._getAssemblyExports = getAssemblyExports;
			if (LogProfilerSupport._logProfilerEnabled) {

				this.attachHotKey();

				setInterval(async () => {
					await this.ensureInitializeProfilerMethods();
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

		private async ensureInitializeProfilerMethods() {
			if (LogProfilerSupport._logProfilerEnabled && !this._flushLogProfile) {

				let profilerExports = await this._getAssemblyExports("Uno.Wasm.LogProfiler");

				this._flushLogProfile = profilerExports.Uno.LogProfilerSupport.FlushProfile;
				this._getLogProfilerProfileOutputFile = profilerExports.no.LogProfilerSupport.GetProfilerProfileOutputFile;
				this._triggerHeapShotLogProfiler = profilerExports.Uno.LogProfilerSupport.TriggerHeapShot;
			}
		}

		private async takeHeapShot() {
			await this.ensureInitializeProfilerMethods();
			this._triggerHeapShotLogProfiler();
		}

		private async readProfileFile() {
			await this.ensureInitializeProfilerMethods();
			this._flushLogProfile();
			var profileFilePath = this._getLogProfilerProfileOutputFile();

			var stat = FS.stat(profileFilePath);

			if (stat && stat.size > 0) {
				return FS.readFile(profileFilePath);
			}
			else {
				console.debug(`Unable to fetch the profile file ${profileFilePath} as it is empty`);
				return null;
			}
		}

		private async saveLogProfile() {
			await this.ensureInitializeProfilerMethods();
			var profileArray = await this.readProfileFile();

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
