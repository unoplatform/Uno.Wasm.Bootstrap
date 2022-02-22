namespace Uno.WebAssembly.Bootstrap {

	export interface UnoConfig {
		uno_remote_managedpath: string;

		uno_app_base: string;

		uno_dependencies: string[];

		uno_main: string;

		assemblyFileExtension: string;

		mono_wasm_runtime: string;

		mono_wasm_runtime_size?: number;

		assemblies_with_size?: {
			[i: string]: number;
		};

		files_integrity?: {
			[i: string]: string;
		};

		total_assemblies_size?: number;

		enable_pwa: boolean;

		offline_files: string[];

		uno_shell_mode: string;

		environmentVariables?: {
			[i: string]: string;
		};

		generate_aot_profile?: boolean;
		enable_debugging?: boolean;
	}
}
