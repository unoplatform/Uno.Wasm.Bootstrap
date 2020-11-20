#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <dlfcn.h>

#define WASM_EXPORT __attribute__((visibility("default")))

int test_add(int a, int b);
float test_add_float(float a, float b);
typedef int (*test_add_func)(int, int);
typedef float (*test_add_float_func)(float, float);
typedef double (*test_add_double_func)(double, double);

WASM_EXPORT int main(int argc, char** argv) {
	void* hModule = dlopen("side.wasm", 0);

	test_add_float_func test_add_float_ptr = (test_add_float_func)dlsym(hModule, "test_add_float");
	float res2 = test_add_float_ptr(22.1f, 22.1f);
}

