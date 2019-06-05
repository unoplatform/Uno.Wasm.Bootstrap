#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#define WASM_EXPORT __attribute__((visibility("default")))

extern "C" {
	WASM_EXPORT int test_add(int a, int b);
	WASM_EXPORT float test_add_float(float a, float b);
	WASM_EXPORT double test_add_double(double a, double b);
	WASM_EXPORT int test_exception();
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
