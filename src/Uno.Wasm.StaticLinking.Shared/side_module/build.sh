#!/bin/bash
emcc main.cpp -s MAIN_MODULE=1 -o hello.html -s WASM=1 -s EXPORT_ALL=1 --pre-js pre.js

buildSideModule(){
	mkdir -p `dirname $2`
	emcc $1 -std=c++17 -s LEGALIZE_JS_FFI=0 -r -o $2 -s WASM=1 -s DISABLE_EXCEPTION_CATCHING=0 -DCUSTOM_VERSION="\"$3\"" -DCUSTOM_FUNCTION_NAME="$4_getCustomVersion"
}

buildSideModule "mysideModule.cpp" "../native/side.bc" "1.2" "side"

buildSideModule "version_test.cpp" "../native/side2.bc" "1.3" "side2"

buildSideModule "version_test.cpp" "../native/side3.bc/1.3/side3.bc" "1.3" "side3"
buildSideModule "version_test.cpp" "../native/side3.bc/1.4/side3.bc" "1.4" "side3"

buildSideModule "version_test.cpp" "../native/side4.bc/1.3/side4.bc" "1.3" "side4"
buildSideModule "version_test.cpp" "../native/side4.bc/2.0/side4.bc" "2.0" "side4"
buildSideModule "version_test.cpp" "../native/side4.bc/3.0/side4.bc" "3.0" "side4"
buildSideModule "version_test.cpp" "../native/side4.bc/5.0/side4.bc" "5.0" "side4"
