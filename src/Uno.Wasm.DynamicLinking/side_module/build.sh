#!/bin/bash
emcc main.cpp -s MAIN_MODULE=1 -o hello.html -s WASM=1 -s EXPORT_ALL=1 --pre-js pre.js

EMCC_FORCE_STDLIBS=1 emcc mysideModule.cpp -std=c++17 -s SIDE_MODULE=1 -s LEGALIZE_JS_FFI=0 -o ../side.wasm -s WASM=1 -s EXPORT_ALL=1 -s DISABLE_EXCEPTION_CATCHING=0
emcc mysideModule.cpp -std=c++17 -s LEGALIZE_JS_FFI=0 -o ../side.bc -s WASM=1 -s DISABLE_EXCEPTION_CATCHING=0
