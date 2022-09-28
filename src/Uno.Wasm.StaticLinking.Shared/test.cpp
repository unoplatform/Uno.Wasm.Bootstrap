#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <GLES2/gl2.h>
#include <GLES2/gl2ext.h>
#include <SDL/SDL.h>

#define WASM_EXPORT __attribute__((visibility("default")))

extern "C" {
	WASM_EXPORT int additional_native_add(int a, int b);
}

WASM_EXPORT int additional_native_add(int a, int b) {
	printf("additional_native_add(%d, %d)\r\n", a, b);
	return a + b;
}

WASM_EXPORT int test_gl() {
	GLuint programObject;
	glGetString(GL_VENDOR);
	SDL_Init(SDL_INIT_VIDEO);
}
