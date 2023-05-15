// Based on https://github.com/dotnet/runtime/blob/ca82565a60380bf4220255c65e493deb44314346/src/mono/wasm/runtime/dotnet.d.ts

declare function mono_wasm_init_aot_profiler(js_obj: any): void;

declare type _MemOffset = number | VoidPtr | NativePointer;
declare function setU8(offset: _MemOffset, value: number): void;
declare function setU16(offset: _MemOffset, value: number): void;
declare function setU32(offset: _MemOffset, value: number): void;
declare function setI8(offset: _MemOffset, value: number): void;
declare function setI16(offset: _MemOffset, value: number): void;
declare function setI32(offset: _MemOffset, value: number): void;
declare function setI64(offset: _MemOffset, value: number): void;
declare function setF32(offset: _MemOffset, value: number): void;
declare function setF64(offset: _MemOffset, value: number): void;
declare function getU8(offset: _MemOffset): number;
declare function getU16(offset: _MemOffset): number;
declare function getU32(offset: _MemOffset): number;
declare function getI8(offset: _MemOffset): number;
declare function getI16(offset: _MemOffset): number;
declare function getI32(offset: _MemOffset): number;
declare function getI64(offset: _MemOffset): number;
declare function getF32(offset: _MemOffset): number;
declare function getF64(offset: _MemOffset): number;

declare function mono_wasm_setenv(name: string, value: string): void;

declare const MONO: {
	mono_wasm_setenv: typeof mono_wasm_setenv;
	config: MonoConfig;
	loaded_files: string[];
	setI8: typeof setI8;
	setI16: typeof setI16;
	setI32: typeof setI32;
	setI64: typeof setI64;
	setU8: typeof setU8;
	setU16: typeof setU16;
	setU32: typeof setU32;
	setF32: typeof setF32;
	setF64: typeof setF64;
	getI8: typeof getI8;
	getI16: typeof getI16;
	getI32: typeof getI32;
	getI64: typeof getI64;
	getU8: typeof getU8;
	getU16: typeof getU16;
	getU32: typeof getU32;
	getF32: typeof getF32;
	getF64: typeof getF64;

	mono_wasm_init_aot_profiler: typeof mono_wasm_init_aot_profiler;
};

declare function mono_bind_static_method(fqn: string, signature?: string): Function;
declare function conv_string(mono_obj: any): string | null;

declare type MONOType = typeof MONO;
declare const BINDING: {
	bind_static_method: typeof mono_bind_static_method;
	conv_string: typeof conv_string;
};
declare type BINDINGType = typeof BINDING;
interface DotnetPublicAPI {
	MONO: typeof MONO;
	BINDING: typeof BINDING;
	INTERNAL: any;
	Module: EmscriptenModule;
	RuntimeId: number;
	RuntimeBuildInfo: {
		ProductVersion: string;
		Configuration: string;
	};
}

declare type AOTProfilerOptions = {
	writeAt?: string;
	sendTo?: string;
};
declare type CoverageProfilerOptions = {
	writeAt?: string;
	sendTo?: string;
};
declare type LogProfilerOptions = {
	configuration?: string, //  log profiler options string"
}
