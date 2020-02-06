#!/bin/bash
emcc main.cpp -s MAIN_MODULE=1 -o hello.html -s WASM=1 -s EXPORT_ALL=1 --pre-js pre.js

emcc mysideModule.cpp -std=c++17 -s LEGALIZE_JS_FFI=0 -r -o ../side.bc -s WASM=1 -s DISABLE_EXCEPTION_CATCHING=0
