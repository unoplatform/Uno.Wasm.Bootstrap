#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <png.h>

#define WASM_EXPORT __attribute__((visibility("default")))

extern "C" {
	WASM_EXPORT const char* CUSTOM_FUNCTION_NAME() {
		auto version = (char*)::malloc(strlen(CUSTOM_VERSION) + 1);
		strcpy(version, CUSTOM_VERSION);
		return version;
	}
}
