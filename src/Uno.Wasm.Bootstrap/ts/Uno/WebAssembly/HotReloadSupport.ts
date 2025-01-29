
declare module "*blazor-hotreload.js" {
	const value: any;
	export default value;
}

namespace Uno.WebAssembly.Bootstrap {

	export class HotReloadSupport {
		private _context?: DotnetPublicAPI;
		private _unoConfig?: UnoConfig;

        static _getApplyUpdateCapabilitiesMethod: any;
        static _applyHotReloadDeltaMethod: any;
        static _initializeMethod: any;

		constructor(context: DotnetPublicAPI, unoConfig: UnoConfig) {
			this._context = context;
			this._unoConfig = unoConfig;
		}

		public static async tryInitializeExports(getAssemblyExports: any) {
			let exports = await getAssemblyExports("Uno.Wasm.MetadataUpdater");

			this._getApplyUpdateCapabilitiesMethod = exports.Uno.Wasm.MetadataUpdate.WebAssemblyHotReload.GetApplyUpdateCapabilities;
			this._applyHotReloadDeltaMethod = exports.Uno.Wasm.MetadataUpdate.WebAssemblyHotReload.ApplyHotReloadDelta;
			this._initializeMethod = exports.Uno.Wasm.MetadataUpdate.WebAssemblyHotReload.Initialize;
		}

		public async initializeHotReload(): Promise<void> {

			const webAppBasePath = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_WEBAPP_BASE_PATH"];

			// This assumes that the runtime includes the header as an env var:
			// https://github.com/dotnet/runtime/blob/bcc0e894256b22736be3ffac876c7f21cffe070a/src/mono/browser/runtime/loader/config.ts#L332
			let browserToolsVariable =
				(<any>this._context).config.environmentVariables['ASPNETCORE-BROWSER-TOOLS']
				|| (<any>this._context).config.environmentVariables['__ASPNETCORE_BROWSER_TOOLS'];

			// Take the place of the internal .NET for WebAssembly APIs for metadata updates coming
			// from the "BrowserLink" feature.
			(function (Blazor) {
				Blazor._internal = {
					initialize: function () {
						if (!HotReloadSupport._initializeMethod()) {
							console.warn("The application was compiled with the IL linker enabled, hot reload is disabled. See https://aka.platform.uno/wasm-il-linker for more details.");
						}
					},

					applyExisting: async function (): Promise<void> {

						if (browserToolsVariable == "true")
						{
							try {
								var m = <any>await import(`/_framework/blazor-hotreload.js`);
								await m.receiveHotReloadAsync();
							}
							catch (e) {
								console.error(`Failed to apply initial metadata delta ${e}`);
							}
						}
					},

					getApplyUpdateCapabilities: function () {
						Blazor._internal.initialize();
						return HotReloadSupport._getApplyUpdateCapabilitiesMethod();
					},

					applyHotReload: function (moduleId: any, metadataDelta: any, ilDelta: any, pdbDelta: any, updatedTypes: any) {
						Blazor._internal.initialize();
						return HotReloadSupport._applyHotReloadDeltaMethod(moduleId, metadataDelta, ilDelta, pdbDelta || "", updatedTypes || []);
					}
				};
			})((<any>window).Blazor || ((<any>window).Blazor = {}));
				
			// Apply pending updates caused by a browser refresh
			(<any>window).Blazor._internal.initialize();
			await (<any>window).Blazor._internal.applyExisting();
		}
	}
}
