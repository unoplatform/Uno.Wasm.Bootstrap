#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <png.h>

#define WASM_EXPORT __attribute__((visibility("default")))

extern "C" {
	WASM_EXPORT int test_add(int a, int b);
	WASM_EXPORT float test_add_float(float a, float b);
	WASM_EXPORT double test_add_double(double a, double b);
	WASM_EXPORT int test_exception();
	WASM_EXPORT void test_png();
}

WASM_EXPORT int test_add(int a, int b) {
	printf("test_add(%d, %d)\r\n", a, b);

	return a + b;
}

WASM_EXPORT float test_add_float(float a, float b) {
	printf("test_add_float(%f, %f)\r\n", a, b);

	return a + b;
}

WASM_EXPORT double test_add_double(double a, double b) {
	printf("test_add_double(%f, %f)\r\n", a, b);

	return a + b;
}

WASM_EXPORT int test_exception() {

	try {
		printf("before exception\r\n");

		throw 21;

		printf("after exception\r\n");
	}
	catch (int error) {
		printf("catch block: %d\r\n", error);
	}

	printf("After block\r\n");
	return 42;
}

WASM_EXPORT void test_png() {
	// Disabled for emsdk 3.1.34
	//
	//   "emsdk/emsdk-3.1.34/emsdk/upstream/bin\wasm-ld.exe" @C:\Users\jerome.uno\AppData\Local\Temp\emscripten__pcr75u2.rsp.utf-8
	//   LLVM ERROR : Cannot select : 0x2700a022d48 : ch = catchret 0x2700844b498, BasicBlock : ch< 0x2700a020a30>, BasicBlock : ch< 0x2700a020928>, emsdk\emsdk - 3.1.34\emsdk\upstream\emscripten\cache\ports\libpng\libpng - 1.6.37\png.c:249 : 1
	//   	In function : png_create_png_struct
	//   	PLEASE submit a bug report to https ://github.com/llvm/llvm-project/issues/ and include the crash backtrace.
	//   Stack dump :
	//   0.    Running pass 'Function Pass Manager' on module 'emsdk\\upstream\\emscripten\\cache\\sysroot\\lib\\wasm32-emscripten\\thinlto\\libpng.a(png.c.o at 31350)'.
	//   1.    Running pass 'WebAssembly Instruction Selection' on function '@png_create_png_struct'
	// png_structp png = png_create_read_struct(PNG_LIBPNG_VER_STRING, NULL, NULL, NULL);

	printf("After test_png\r\n");
}
