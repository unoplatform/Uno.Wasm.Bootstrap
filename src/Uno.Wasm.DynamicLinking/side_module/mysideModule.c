#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#define WASM_EXPORT __attribute__((visibility("default")))

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
