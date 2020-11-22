#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <png.h>

#define WASM_EXPORT __attribute__((visibility("default")))

extern "C" {
	WASM_EXPORT const char* CUSTOM_FUNCTION_NAME() { return CUSTOM_VERSION; }
}
