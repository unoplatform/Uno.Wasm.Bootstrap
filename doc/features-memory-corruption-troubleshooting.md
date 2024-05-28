---
uid: UnoWasmBootstrap.Features.MemoryTroubleshooting
---

## Native memory troubleshooting

To enable native memory troubleshooting, it is possible to use [LLVM's sanitizer](https://emscripten.org/docs/debugging/Sanitizers.html) feature.

To enable it, add the following to your project file:

```xml
<ItemGroup>
	<WasmShellExtraEmccFlags Include="-fsanitize=address" />
</ItemGroup>
```

This will allow for malloc/free and other related memory access features to validate for possible issues, like this one:

```
================================================================= dotnet.js:2498:16
==42==ERROR: AddressSanitizer: attempting free on address which was not malloc()-ed: 0x03116d80 in thread T0 dotnet.js:2498:16
    #0 0x1657f6 in free+0x1657f6 (http://localhost:57998/dotnet.wasm+0x1657f6) dotnet.js:2498:16
    #1 0x12eb3a in monoeg_g_free+0x12eb3a (http://localhost:57998/dotnet.wasm+0x12eb3a) dotnet.js:2498:16
    #2 0x19936 in ves_pinvoke_method+0x19936 (http://localhost:57998/dotnet.wasm+0x19936) dotnet.js:2498:16
    #3 0xb8a5 in interp_exec_method+0xb8a5 (http://localhost:57998/dotnet.wasm+0xb8a5) dotnet.js:2498:16
    #4 0xa0bb in interp_runtime_invoke+0xa0bb (http://localhost:57998/dotnet.wasm+0xa0bb) dotnet.js:2498:16
    #5 0x52fcf in mono_jit_runtime_invoke+0x52fcf (http://localhost:57998/dotnet.wasm+0x52fcf) dotnet.js:2498:16
    #6 0xc6a6f in do_runtime_invoke+0xc6a6f (http://localhost:57998/dotnet.wasm+0xc6a6f) dotnet.js:2498:16
    #7 0xc711a in mono_runtime_try_invoke+0xc711a (http://localhost:57998/dotnet.wasm+0xc711a) dotnet.js:2498:16
    #8 0xc9234 in mono_runtime_invoke+0xc9234 (http://localhost:57998/dotnet.wasm+0xc9234) dotnet.js:2498:16
    #9 0x7967 in mono_wasm_invoke_method+0x7967 (http://localhost:57998/dotnet.wasm+0x7967) dotnet.js:2498:16
    #10 0x80002ffa in Module._mono_wasm_invoke_method http://localhost:57998/dotnet.js:12282:51 dotnet.js:2498:16
    #11 0x800002e9 in ccall http://localhost:57998/dotnet.js:745:18 dotnet.js:2498:16
    #12 0x800002f4 in cwrap/< http://localhost:57998/dotnet.js:756:12
```

Showing that mono is trying to free some memory pointer that was never returned by `malloc`.

Note that the runtime performance is severely degraded when enabling this feature.

### musl weak aliases override

In some cases, the [musl library](https://github.com/emscripten-core/emscripten/tree/aa66f92b790c8d319d1599d44657a6305bf74a8a/system/lib/libc/musl) performs out-of-bounds reads which can be [detected as false positives](https://github.com/emscripten-core/emscripten/issues/7279#issuecomment-734934021) by ASAN's checkers.

In such cases, providing alternate non-overflowing versions of [weak aliased MUSL functions](https://github.com/emscripten-core/emscripten/blob/aa66f92b790c8d319d1599d44657a6305bf74a8a/system/lib/libc/musl/src/string/stpncpy.c#L31) is possible through the `WasmShellNativeCompile` feature of the bootstrapper.

For example, `strncpy` can be overriden by a custom implementation as follows:

```c
char *__stpncpy(char *restrict d, const char *restrict s, size_t n)
{
	for (; n && (*d=*s); n--, s++, d++);
	memset(d, 0, n);
	return d;
}
```

in a C file defined in your project and imported through `WasmShellNativeCompile`.
