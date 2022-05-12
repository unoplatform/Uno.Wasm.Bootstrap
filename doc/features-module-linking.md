### WebAssembly Module Linking support

#### Static Linking overview
Statically linking Emscripten LLVM Bitcode (`.bc` and `.a` files) files to mono is supported on both Windows 10 and Linux. To build on Windows please refer to the AOT environment setup instructions.

This linking type embeds the `.bc` or `.a` files with the rest of the WebAssembly modules, and uses _normal_ webassembly function invocations that are faster than with dynamic linking.

Any `.bc` or `.a` file placed as `content` in the built project will be statically linked to the currently running application.

This allowing for p/invoke to be functional when resolving methods from the loaded module. If you have a `.bc` or a `.a` file you don't want to be include in the linking, you may add the `UnoAotCompile="false"` metadata that way:

``` xml
<ItemGroup>
    <!-- Deactivate the discovery of a .bc or a .a file for static linking -->
    <Content Update="path\to\my\file.bc" UnoAotCompile="False" />
</ItemGroup>
```

#### Static Linking multi-version support
As emscripten's ABI is not guaranteed to be compatible between versions, it may also be required to include multiple versions of the same LLVM binaries, compiled against different versions of LLVM.
In order to enable this scenario, the Uno Bootstrapper supports adding .bc files by convention.

If the bitcode file to be added is named `libTest.bc`, the following structure can be used in your project:
- `libTest.bc/2.0.6/libTest.bc`
- `libTest.bc/2.0.9/libTest.bc`

In this case, based on the emscripten version used by the mono runtime, the bootstrapper will choose the closest matching version.

#### Static Linking additional emcc flags
Static linking may also require some additional emscripten flags, for instance when using libpng. In such a case, add the following to your project:

```xml
<ItemGroup>
	<WasmShellExtraEmccFlags Include="-s USE_LIBPNG=1"/>
</ItemGroup>
```

For more information, see the `Uno.Wasm.StaticLinking.Aot` sample side module build script.

### Static linking additional P/Invoke libraries

When building applications, in some cases, NuGet provided libraries may use native dependencies that are emscripten provided libraries, such as `libc`.

In such cases, the boostrapper allows for providing a set of known P/Invoke libraries as follows:

```xml
<ItemGroup>
    <WasmShellAdditionalPInvokeLibrary Include="libc" />
</ItemGroup>
```

It's important to note that providing additional libraries this way implies that all the imported functions will have to be available during emcc link operation.

Any missing function will result in a missing symbol error.

### Additional C/C++ files

The bootstrapper provides the ability to include additional C/C++ files to the final package generation. 

This feature can be used to include additional source files for native operations that may be more difficult to perform from managed C# code, but can also be used to override some weak aliases with [ASAN](https://emscripten.org/docs/debugging/Sanitizers.html).

#### Usage
```xml
<ItemGroup>
    <WasmShellNativeCompile Include="myfile.cpp" />
</ItemGroup>
```

The file is provided as-is to `emcc` and its resulting object file is linked with the rest of the compilation.

This feature is meant to be used for small additions of native code. If more is needed (e.g. adding header directories, defines, options, etc...) it is best to use the emcc tooling directly.

#### Example

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

#### Invoking emscripten and Mono/.NET 5 native functions

In order to invoke emscripten and mono native functions, the bootstrapper exposes the special library name `__Native`. 

For instance, the following enables the mono internal tracing:

```
static class MonoInternals
{
	[DllImport("__Native")]
	internal static extern void mono_trace_enable(int enable);
}
```

Or use emscripten functions:

```
[DllImport("__Internal_emscripten")]
public static extern void emscripten_console_log(string str);
