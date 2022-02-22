namespace Uno.WebAssembly.Bootstrap {

	export class HotReloadSupport {
		private _context?: DotnetPublicAPI;

		constructor(context: DotnetPublicAPI) {
			this._context = context;
		}

		public async initializeHotReload(): Promise<void> {

			// Take the place of the internal .NET for WebAssembly APIs for metadata updates coming
			// from the "BrowserLink" feature.

			(function (Blazor) {
				Blazor._internal = {
					initialize: function (BINDING: any) {
						if (!this.getApplyUpdateCapabilitiesMethod) {
							this.getApplyUpdateCapabilitiesMethod = BINDING.bind_static_method("[Uno.Wasm.MetadataUpdater] Uno.Wasm.MetadataUpdate.WebAssemblyHotReload:GetApplyUpdateCapabilities");
							this.applyHotReloadDeltaMethod = BINDING.bind_static_method("[Uno.Wasm.MetadataUpdater] Uno.Wasm.MetadataUpdate.WebAssemblyHotReload:ApplyHotReloadDelta");
							this.initializeMethod = BINDING.bind_static_method("[Uno.Wasm.MetadataUpdater] Uno.Wasm.MetadataUpdate.WebAssemblyHotReload:Initialize");
						}

						this.initializeMethod();
					},

					applyExisting: async function (): Promise<void> {
						const webAppBasePath = this._unoConfig.environmentVariables["UNO_BOOTSTRAP_WEBAPP_BASE_PATH"];

						var hotreloadConfigResponse = await fetch(`/_framework/unohotreload`);

						var modifiableAssemblies = hotreloadConfigResponse.headers.get('DOTNET-MODIFIABLE-ASSEMBLIES');
						var aspnetCoreBrowserTools = hotreloadConfigResponse.headers.get('ASPNETCORE-BROWSER-TOOLS');

						if (modifiableAssemblies) {
							MONO.mono_wasm_setenv('DOTNET_MODIFIABLE_ASSEMBLIES', modifiableAssemblies);
						}

						// To uncomment once https://github.com/dotnet/aspnetcore/issues/37357#issuecomment-941237000 is released
						// if (aspnetCoreBrowserTools == "true")
						{
							try {
								var m = await eval("import(`/_framework/blazor-hotreload.js`)");
								m.receiveHotReload();
							}
							catch (e) {
								console.error(`Failed to apply initial metadata delta ${e}`);
							}
						}
					},

					getApplyUpdateCapabilities: function () {
						this.initialize();
						return this.getApplyUpdateCapabilitiesMethod();
					},

					applyHotReload: function (moduleId: any, metadataDelta: any, ilDelta: any) {
						this.initialize();
						return this.applyHotReloadDeltaMethod(moduleId, metadataDelta, ilDelta);
					}
				};
			})((<any>window).Blazor || ((<any>window).Blazor = {}));

			// Apply pending updates caused by a browser refresh
			(<any>window).Blazor._internal.initialize(this._context.BINDING);
			await (<any>window).Blazor._internal.applyExisting();
		}
	}
}
