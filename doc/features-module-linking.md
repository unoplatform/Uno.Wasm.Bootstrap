---
uid: UnoWasmBootstrap.ModuleLinking
---

# WebAssembly Module Linking support

## Static Linking overview

Statically linking Emscripten LLVM Bitcode (`.o` and `.a` files) files to mono is supported on both Windows 10 and Linux. To build on Windows please refer to the AOT environment setup instructions.

This linking type embeds the `.o` or `.a` files with the rest of the WebAssembly modules, and uses _normal_ webassembly function invocations that are faster than with dynamic linking.

Any `.o` or `.a` file placed as `content` in the built project will be statically linked to the currently running application.

This allowing for p/invoke to be functional when resolving methods from the loaded module. If you have a `.o` or a `.a` file you don't want to be include in the linking, you may add the `UnoAotCompile="false"` metadata that way:

```xml
<ItemGroup>
    <!-- Deactivate the discovery of a .o or a .a file for static linking -->
    <Content Update="path\to\my\file.a" UnoAotCompile="False" />
</ItemGroup>
```

The .NET SDK [`NativeFileReference`](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies) is also supported.

## WebAssembly Exceptions support

As of version 7.0 and later, WebAssembly Exceptions support is now required.

If you're building C++ files with exceptions support, you'll need to add the [emcc `-fwasm-exceptions` option](https://emscripten.org/docs/porting/exceptions.html#webassembly-exception-handling-proposal) and remove the `-s DISABLE_EXCEPTION_CATCHING=0` if you specified it.

If you try building with a mix of options, you'll get emcc messages like this one:

```console
error: DISABLE_EXCEPTION_THROWING was set (likely due to -fno-exceptions), which means no C++ exception throwing 
       support code is linked in, but exception catching code appears. Either do not set DISABLE_EXCEPTION_THROWING
       (if you do want exception throwing) or compile all source files with -fno-except (so that no exceptions support
       code is required); also make sure DISABLE_EXCEPTION_CATCHING is set to the right value - if you want exceptions, 
       it should be off, and vice versa.
```

## Static Linking multi-version support

As emscripten's ABI is not guaranteed to be compatible between versions, it may also be required to include multiple versions of the same LLVM binaries, compiled against different versions of LLVM. In order to enable this scenario, the Uno Bootstrapper supports adding .a files by convention.

If the bitcode file to be added is named `libTest.o` or `libTest.a`, the following structure can be used in a project:

| File path                           | Description                                                                                           |
|-------------------------------------|-------------------------------------------------------------------------------------------------------|
| `libTest.a/2.0.6/libTest.a`         | Emscripten 2.0.6 to 2.0.8, single threaded (Bootstrapper 3.3 and earlier format)                      |
| `libTest.a/2.0.9/libTest.a`         | Emscripten 2.0.9 and later, single threaded (Bootstrapper 3.3 and earlier format)                     |
| `libTest.a/2.0.6/st/libTest.a`      | Emscripten 2.0.6 and later, single threaded                                                           |
| `libTest.a/2.0.9/st/libTest.a`      | Emscripten 2.0.9 and later, single threaded                                                           |
| `libTest.a/2.0.6/mt/libTest.a`      | Emscripten 2.0.6 and later, multi threaded                                                            |
| `libTest.a/2.0.9/mt/libTest.a`      | Emscripten 2.0.9 and later, multi threaded                                                            |
| `libTest.a/2.0.6/st,simd/libTest.a` | Emscripten 2.0.6 and later, single threaded with SIMD                                                 |
| `libTest.a/2.0.9/st,simd/libTest.a` | Emscripten 2.0.9 and later, single threaded with SIMD                                                 |
| `libTest.a/2.0.6/mt,simd/libTest.a` | Emscripten 2.0.6 and later, multi threaded with SIMD                                                  |
| `libTest.a/2.0.9/mt,simd/libTest.a` | Emscripten 2.0.9 and later, multi threaded with SIMD                                                  |

Based on the emscripten version used by the .NET runtime and the enabled runtime features, the bootstrapper will choose the closest matching version.

As of bootstrapper 7.0, the following runtime features are supported:

- `st` for Single Threaded runtime
- `mt` for Multi Threaded runtime
- `simd` for SIMD enabled runtime

## Static Linking additional emcc flags

Static linking may also require some additional emscripten flags, for instance when using libpng. In such a case, add the following to your project:

```xml
<ItemGroup>
    <WasmShellExtraEmccFlags Include="-s USE_LIBPNG=1"/>
</ItemGroup>
```

For more information, see the `Uno.Wasm.StaticLinking.Aot` sample side module build script.

## Static linking additional P/Invoke libraries

When building applications, in some cases, NuGet provided libraries may use native dependencies that are emscripten provided libraries, such as `libc`.

In such cases, the bootstrapper allows for providing a set of known P/Invoke libraries as follows:

```xml
<ItemGroup>
    <WasmShellAdditionalPInvokeLibrary Include="libc.so" />
</ItemGroup>
```

> [!NOTE]
> Starting with .NET 7, the libc DllImport is special and needs to be called `libc.so`. This is not needed for other library names.

It's important to note that providing additional libraries this way implies that all the imported functions will have to be available during emcc link operation.

Any missing function will result in a missing symbol error.

## Additional C/C++ files

The bootstrapper provides the ability to include additional C/C++ files to the final package generation.

This feature can be used to include additional source files for native operations that may be more difficult to perform from managed C# code, but can also be used to override some weak aliases with [ASAN](https://emscripten.org/docs/debugging/Sanitizers.html).

### Usage

```xml
<ItemGroup>
    <WasmShellNativeCompile Include="myfile.cpp" />
</ItemGroup>
```

The file is provided as-is to `emcc` and its resulting object file is linked with the rest of the compilation.

This feature is meant to be used for small additions of native code. If more is needed (e.g. adding header directories, defines, options, etc...) it is best to use the emcc tooling directly.

The .NET SDK [`NativeFileReference`](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies) is also supported to include C/C++ source files.

### Example

Here's an example of file:

```cpp
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#define WASM_EXPORT __attribute__((visibility("default")))

extern "C" {
    WASM_EXPORT int additional_native_add(int a, int b);
}

WASM_EXPORT int additional_native_add(int a, int b) {
    printf("test_add(%d, %d)\r\n", a, b);
    return a + b;
}
```

which can be used in C# as follows:

```csharp
static class AdditionalImportTest
{
    [DllImport("__Native")]
    internal static extern int additional_native_add(int left, int right);
}
```

## Invoking emscripten and Mono/.NET 5 native functions

In order to invoke emscripten and mono native functions, the bootstrapper exposes the special library name `__Native`.

> [!NOTE]
> In order for `__Native` to be available, you'll need to specify `<WasmShellAdditionalPInvokeLibrary Include="__Native" />` as explained in the sections above.

For instance, the following enables the mono internal tracing:

```csharp
static class MonoInternals
{
    [DllImport("__Native")]
    internal static extern void mono_trace_enable(int enable);
}
```

Or use emscripten functions:

```csharp
    [DllImport("__Internal_emscripten")]
    public static extern void emscripten_console_log(string str);
```

## Exposing additional methods from emscripten

When interoperating with native features, such as OpenGL, it may be needed to expose additional features from emscripten.

The `EXPORTED_RUNTIME_METHODS` flag can be filled through the following msbuild items:

```xml
<ItemGroup>
    <WasmShellEmccExportedRuntimeMethod Include="GL" />
</ItemGroup>
```
