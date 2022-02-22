/// <reference path="AotProfilerSupport.ts"/>
/// <reference path="HotReloadSupport.ts"/>
/// <reference path="LogProfilerSupport.ts"/>
/// <reference path="UnoConfig.ts"/>

namespace Uno.WebAssembly.Bootstrap {

	/**
	 *  This block is present for backward compatibility when "MonoRuntime" was provided by mono-wasm.
	 * */
	export class MonoRuntimeCompatibility {
        static load_runtime: any;
        static assembly_load: any;
        static find_class: any;
        static find_method: any;
        static invoke_method: any;
        static mono_string_get_utf8: any;
        static mono_wasm_obj_array_new: any;
        static mono_string: any;
        static mono_wasm_obj_array_set: any;

		public static initialize() {
			MonoRuntimeCompatibility.load_runtime = Module.cwrap("mono_wasm_load_runtime", null, ["string", "number"]);
			MonoRuntimeCompatibility.assembly_load = Module.cwrap("mono_wasm_assembly_load", "number", ["string"]);
			MonoRuntimeCompatibility.find_class = Module.cwrap("mono_wasm_assembly_find_class", "number", ["number", "string", "string"]);
			MonoRuntimeCompatibility.find_method = Module.cwrap("mono_wasm_assembly_find_method", "number", ["number", "string", "number"]);
			MonoRuntimeCompatibility.invoke_method = Module.cwrap("mono_wasm_invoke_method", "number", ["number", "number", "number"]);
			MonoRuntimeCompatibility.mono_string_get_utf8 = Module.cwrap("mono_wasm_string_get_utf8", "number", ["number"]);
			MonoRuntimeCompatibility.mono_string = Module.cwrap("mono_wasm_string_from_js", "number", ["string"]);
			MonoRuntimeCompatibility.mono_wasm_obj_array_new = Module.cwrap("mono_wasm_obj_array_new", "number", ["number"]);
			MonoRuntimeCompatibility.mono_wasm_obj_array_set = Module.cwrap("mono_wasm_obj_array_set", null, ["number", "number", "number"]);
		}

		public static conv_string(mono_obj: any) {
			if (mono_obj === 0)
				return null;

			const raw = MonoRuntimeCompatibility.mono_string_get_utf8(mono_obj);
			const res = Module.UTF8ToString(raw);
			Module._free(raw);

			return res;
		}

		public static call_method(method: any, this_arg: any, args: any) {
			const args_mem = Module._malloc(args.length * 4);
			const eh_throw = Module._malloc(4);
			for (let i = 0; i < args.length; ++i)
				Module.setValue(args_mem + i * 4, args[i], "i32");
			Module.setValue(eh_throw, 0, "i32");

			const res = MonoRuntimeCompatibility.invoke_method(method, this_arg, args_mem, eh_throw);

			const eh_res = Module.getValue(eh_throw, "i32");

			Module._free(args_mem);
			Module._free(eh_throw);

			if (eh_res !== 0) {
				const msg = MonoRuntimeCompatibility.conv_string(res);
				throw new Error(msg);
			}

			return res;
		}
	}
}
