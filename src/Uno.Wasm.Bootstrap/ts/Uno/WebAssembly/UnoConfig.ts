namespace Uno.WebAssembly.Bootstrap {

	export interface UnoConfig {
		uno_remote_managedpath: string;

		uno_app_base: string;

		uno_dependencies: string[];

		uno_main: string;

		assemblyFileExtension: string;

		assemblyFileNameObfuscationMode: string;

		enable_pwa: boolean;

		offline_files: string[];

		emcc_exported_runtime_methods?: string[];

		uno_shell_mode: string;

		environmentVariables?: {
			[i: string]: string;
		};

		generate_aot_profile?: boolean;

		uno_enable_tracing?: boolean;

		uno_debugging_enabled?: boolean;

		uno_runtime_options?: string[];
	}
}
