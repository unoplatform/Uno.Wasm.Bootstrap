
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

			// Take the place of the internal .NET for WebAssembly APIs for metadata updates coming
			// from the "BrowserLink" feature.
			const boostrapContext = this._context;

			(function (Blazor) {
				Blazor._internal = {
					initialize: function () {
						if (!HotReloadSupport._initializeMethod()) {
							console.warn("The application was compiled with the IL linker enabled, hot reload is disabled. See https://aka.platform.uno/wasm-il-linker for more details.");
						}
					},

					applyExisting: async function (): Promise<void> {

						var hotreloadConfigResponse = await fetch(`/_framework/unohotreload`);

						var modifiableAssemblies = hotreloadConfigResponse.headers.get('DOTNET-MODIFIABLE-ASSEMBLIES');
						var aspnetCoreBrowserTools = hotreloadConfigResponse.headers.get('ASPNETCORE-BROWSER-TOOLS');

						if (modifiableAssemblies) {
							boostrapContext.MONO.mono_wasm_setenv('DOTNET_MODIFIABLE_ASSEMBLIES', modifiableAssemblies);
						}

						// To uncomment once https://github.com/dotnet/aspnetcore/issues/37357#issuecomment-941237000 is released
						// if (aspnetCoreBrowserTools == "true")
						{
							try {
								var m = <any>await import(`/_framework/blazor-hotreload.js`);
								m.receiveHotReload();
							}
							catch (e) {
								console.error(`Failed to apply initial metadata delta ${e}`);
							}
						}
					},

					getApplyUpdateCapabilities: function () {
						this.initialize();
						return HotReloadSupport._getApplyUpdateCapabilitiesMethod();
					},

					applyHotReload: function (moduleId: any, metadataDelta: any, ilDelta: any, pdbDelta: any) {
						this.initialize();
						return HotReloadSupport._applyHotReloadDeltaMethod(moduleId, metadataDelta, ilDelta, pdbDelta || "");
					}
				};
			})((<any>window).Blazor || ((<any>window).Blazor = {}));
				
			// Apply pending updates caused by a browser refresh
			(<any>window).Blazor._internal.initialize(boostrapContext.BINDING);
			await (<any>window).Blazor._internal.applyExisting();
		}
	}
}
